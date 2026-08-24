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
    /// <summary>
    /// <paramref name="entryPrefix"/> qualifica as entradas quando o app vem de
    /// dentro de uma solução: sem isso, dois apps da mesma solução teriam
    /// Controls/1.json idêntico e uma diretiva de supressão em um silenciaria o outro.
    /// </summary>
    public static CanvasApp Extract(
        IArtifactSource source,
        string artifactPath,
        string appName,
        string? entryPrefix = null)
    {
        var app = new CanvasApp
        {
            Name = appName,
            Location = new SourceLocation(artifactPath, string.Empty, null, 0, 0),
        };

        foreach (var entry in ControlEntries(source))
        {
            var control = TryReadScreen(source, artifactPath, entry, entryPrefix);
            if (control is null)
                continue;

            // O objeto App vem num Controls/*.json como qualquer tela, mas não é
            // uma: ele carrega OnStart, OnError e StartScreen. Tratá-lo como tela
            // fazia toda regra que percorre telas examiná-lo, e escopava as
            // variáveis do OnStart numa tela chamada 'App' que não existe.
            if (IsAppObject(control))
                app.AppProperties.AddRange(control.Properties);
            else
                app.Screens.Add(control);
        }

        ReadAppProperties(source, artifactPath, app, entryPrefix);
        ReadDataSources(source, artifactPath, app);
        ReadConnections(source, app);
        ReadComponents(source, artifactPath, app, entryPrefix);

        return app;
    }

    /// <summary>O contêiner das fórmulas do App, e não uma tela.</summary>
    private static bool IsAppObject(Control control) =>
        string.Equals(control.TemplateName, "appinfo", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ControlEntries(IArtifactSource source) =>
        source.Entries
            .Where(e => e.StartsWith("Controls/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

    private static string Qualify(string? prefix, string entry) =>
        string.IsNullOrEmpty(prefix) ? entry : $"{prefix}/{entry}";

    private static Control? TryReadScreen(
        IArtifactSource source, string artifactPath, string entry, string? entryPrefix)
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
            return ReadControl(top, artifactPath, Qualify(entryPrefix, entry), isScreen: true);
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

    /// <summary>
    /// As conexões ficam em Connections/Connections.json, num mapa de GUID para
    /// os dados da conexão. Cada uma registra quais fontes vêm dela e quais
    /// controles dependem dela — é por esses dois números que se sabe se
    /// alguém a usa.
    /// </summary>
    /// <summary>
    /// As definições de componente ficam em Components/*.json, e trazem um
    /// array CustomProperties com o que alguém acrescentou ao componente. As
    /// propriedades embutidas não aparecem ali: elas vêm do template.
    /// </summary>
    private static void ReadComponents(
        IArtifactSource source, string artifactPath, CanvasApp app, string? entryPrefix)
    {
        var entradas = source.Entries
            .Where(e => e.StartsWith("Components/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entradas)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(source.ReadText(entry));
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                var raiz = doc.RootElement.TryGetProperty("TopParent", out var top)
                           && top.ValueKind == JsonValueKind.Object
                    ? top
                    : doc.RootElement;

                if (raiz.ValueKind != JsonValueKind.Object)
                    continue;

                // O nome legível é o TemplateOriginalName; 'Name' costuma ser o
                // GUID interno, que não diz nada a quem lê o achado.
                var nome = GetString(raiz, "TemplateOriginalName")
                           ?? GetString(raiz, "Name")
                           ?? "(sem nome)";

                var componente = new CanvasComponent
                {
                    Name = nome,
                    Location = new SourceLocation(artifactPath, Qualify(entryPrefix, entry), nome, 0, 0),
                };

                if (raiz.TryGetProperty("CustomProperties", out var propriedades)
                    && propriedades.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in propriedades.EnumerateArray())
                    {
                        var propriedade = GetString(p, "Name");
                        if (propriedade is null)
                            continue;

                        componente.CustomProperties.Add(new ComponentProperty(
                            propriedade,
                            GetString(p, "DisplayName") ?? propriedade,
                            GetString(p, "PropertyDataTypeKey") ?? string.Empty));
                    }
                }

                app.Components.Add(componente);
            }
        }
    }

    private static void ReadConnections(IArtifactSource source, CanvasApp app)
    {
        const string entry = "Connections/Connections.json";
        if (!source.Has(entry))
            return;

        try
        {
            using var doc = JsonDocument.Parse(source.ReadText(entry));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            foreach (var item in doc.RootElement.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var referencia = item.Value.TryGetProperty("connectionRef", out var r)
                                 && r.ValueKind == JsonValueKind.Object
                    ? r
                    : default;

                app.Connections.Add(new AppConnection(
                    Id: GetString(item.Value, "id") ?? item.Name,
                    DisplayName: referencia.ValueKind == JsonValueKind.Object
                        ? GetString(referencia, "displayName") ?? item.Name
                        : item.Name,
                    ConnectorId: referencia.ValueKind == JsonValueKind.Object
                        ? GetString(referencia, "id") ?? string.Empty
                        : string.Empty,
                    DataSourceCount: Contar(item.Value, "dataSources"),
                    DependentCount: Contar(item.Value, "dependents")));
            }
        }
        catch (JsonException)
        {
            // Sem conexões legíveis, a regra que depende delas não avalia nada.
        }
    }

    private static int Contar(JsonElement objeto, string propriedade) =>
        objeto.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.GetArrayLength()
            : 0;

    private static void ReadAppProperties(
        IArtifactSource source, string artifactPath, CanvasApp app, string? entryPrefix)
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
                    new SourceLocation(artifactPath, Qualify(entryPrefix, entry), $"App.{name}", 0, 0)));
            }
        }
        catch (JsonException)
        {
            // Properties.json ilegível não impede a análise dos controles.
        }
    }

    /// <summary>
    /// O Studio grava as data sources em References/DataSources.json; versões
    /// mais antigas do formato usavam DataSources/DataSources.json. Aceitamos as duas.
    /// </summary>
    private static readonly string[] DataSourceEntries =
    [
        "References/DataSources.json",
        "DataSources/DataSources.json",
    ];

    private static void ReadDataSources(IArtifactSource source, string artifactPath, CanvasApp app)
    {
        var entry = DataSourceEntries.FirstOrDefault(source.Has);
        if (entry is null)
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
