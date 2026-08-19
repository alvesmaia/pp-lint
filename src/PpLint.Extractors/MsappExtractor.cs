using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Extractors;

/// <summary>
/// Extrai o modelo de um canvas app a partir do pacote .msapp cru,
/// sem depender do pac CLI. Tolerante a campos ausentes e arquivos
/// malformados: o que não puder ser lido é ignorado silenciosamente.
/// </summary>
public static class MsappExtractor
{
    public static CanvasApp Extract(IArtifactSource source, string artifactPath, string appName)
    {
        var app = new CanvasApp
        {
            Name = appName,
            Location = new SourceLocation(artifactPath, string.Empty, null, 0, 0),
        };

        foreach (var entry in ControlEntries(source))
        {
            var screen = TryReadScreen(source, artifactPath, entry);
            if (screen is not null)
                app.Screens.Add(screen);
        }

        ReadAppProperties(source, artifactPath, app);
        ReadDataSources(source, artifactPath, app);

        return app;
    }

    private static IEnumerable<string> ControlEntries(IArtifactSource source) =>
        source.Entries
            .Where(e => e.StartsWith("Controls/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

    private static Control? TryReadScreen(IArtifactSource source, string artifactPath, string entry)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(source.ReadText(entry));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArtifactException)
        {
            return null;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("TopParent", out var top) || top.ValueKind != JsonValueKind.Object)
                return null;
            return ReadControl(top, artifactPath, entry, isScreen: true);
        }
    }

    private static Control ReadControl(JsonElement element, string artifactPath, string entry, bool isScreen)
    {
        var name = GetString(element, "Name") ?? "(sem nome)";
        var template = element.TryGetProperty("Template", out var t) && t.ValueKind == JsonValueKind.Object
            ? GetString(t, "Name") ?? string.Empty
            : string.Empty;

        var control = new Control
        {
            Name = name,
            TemplateName = template,
            IsScreen = isScreen,
            Location = new SourceLocation(artifactPath, entry, name, 0, 0),
        };

        if (element.TryGetProperty("Rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in rules.EnumerateArray())
            {
                var property = GetString(rule, "Property");
                var script = GetString(rule, "InvariantScript");
                if (property is null || script is null)
                    continue;

                control.Properties.Add(new PowerFxProperty(
                    property,
                    script,
                    new SourceLocation(artifactPath, entry, $"{name}.{property}", 0, 0)));
            }
        }

        if (element.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object)
                    control.AddChild(ReadControl(child, artifactPath, entry, isScreen: false));
            }
        }

        return control;
    }

    private static void ReadAppProperties(IArtifactSource source, string artifactPath, CanvasApp app)
    {
        const string entry = "Properties.json";
        if (!source.Has(entry))
            return;

        try
        {
            using var doc = JsonDocument.Parse(source.ReadText(entry));
            foreach (var name in new[] { "OnStart", "OnError", "StartScreen" })
            {
                var script = GetString(doc.RootElement, name);
                if (string.IsNullOrWhiteSpace(script))
                    continue;

                app.AppProperties.Add(new PowerFxProperty(
                    name,
                    script,
                    new SourceLocation(artifactPath, entry, $"App.{name}", 0, 0)));
            }
        }
        catch (JsonException)
        {
            // Properties.json ilegível não impede a análise dos controles.
        }
    }

    private static void ReadDataSources(IArtifactSource source, string artifactPath, CanvasApp app)
    {
        const string entry = "DataSources/DataSources.json";
        if (!source.Has(entry))
            return;

        try
        {
            using var doc = JsonDocument.Parse(source.ReadText(entry));
            if (!doc.RootElement.TryGetProperty("DataSources", out var list) || list.ValueKind != JsonValueKind.Array)
                return;

            foreach (var ds in list.EnumerateArray())
            {
                var name = GetString(ds, "Name");
                if (name is null)
                    continue;

                var kind = GetString(ds, "Type") ?? string.Empty;
                var columns = new List<string>();

                if (ds.TryGetProperty("Columns", out var cols) && cols.ValueKind == JsonValueKind.Array)
                {
                    foreach (var col in cols.EnumerateArray())
                    {
                        var colName = GetString(col, "Name");
                        if (colName is not null)
                            columns.Add(colName);
                    }
                }

                app.DataSources.Add(new DataSource(name, kind, columns));
            }
        }
        catch (JsonException)
        {
            // Sem data sources legíveis, as regras que dependem delas simplesmente não avaliam nada.
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
