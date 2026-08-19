using System.Text.Encodings.Web;
using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;
using PpLint.Rules;

namespace PpLint.Cli;

/// <summary>
/// Saída em SARIF 2.1.0, o formato que o GitHub lê nativamente: subido com a
/// action upload-sarif, cada achado vira anotação no diff do pull request e
/// entrada na aba Security.
/// </summary>
public static class SarifReporter
{
    private const string Repository = "https://github.com/alvesmaia/pp-lint";

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        // Relaxed porque a saída é um arquivo de relatório, não HTML embutido: o
        // encoder padrão escaparia apóstrofo e sinais de menor como 0027 e
        // 003C, e as mensagens citam nomes entre apóstrofos o tempo todo.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Render(AnalysisRun run)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Options))
        {
            w.WriteStartObject();
            w.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
            w.WriteString("version", "2.1.0");

            w.WriteStartArray("runs");
            w.WriteStartObject();

            WriteTool(w, run);
            WriteResults(w, run);

            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteTool(Utf8JsonWriter w, AnalysisRun run)
    {
        w.WriteStartObject("tool");
        w.WriteStartObject("driver");
        w.WriteString("name", "pp-lint");
        w.WriteString("informationUri", Repository);
        w.WriteString("version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");

        w.WriteStartArray("rules");

        // Só as regras que produziram achado. Declarar as 28 num relatório que
        // achou uma polui a aba Security com regras sem resultado.
        var reportadas = run.AllDiagnostics
            .GroupBy(d => d.RuleId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var grupo in reportadas)
            WriteRule(w, grupo.Key, grupo.First());

        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteRule(Utf8JsonWriter w, string ruleId, Diagnostic exemplo)
    {
        var doc = RuleDocs.Find(ruleId);

        w.WriteStartObject();
        w.WriteString("id", ruleId);
        w.WriteString("name", doc?.Title ?? ruleId);

        w.WriteStartObject("shortDescription");
        w.WriteString("text", doc?.Summary ?? doc?.Title ?? ruleId);
        w.WriteEndObject();

        if (doc is not null)
        {
            w.WriteStartObject("fullDescription");
            w.WriteString("text", doc.Summary);
            w.WriteEndObject();

            w.WriteStartObject("help");
            w.WriteString("markdown", doc.Markdown);
            w.WriteString("text", doc.Summary);
            w.WriteEndObject();
        }

        w.WriteString("helpUri", $"{Repository}/blob/main/docs/rules/{ruleId}.md");

        w.WriteStartObject("defaultConfiguration");
        w.WriteString("level", Level(exemplo.Severity));
        w.WriteEndObject();

        w.WriteStartObject("properties");
        w.WriteString("category", exemplo.Category.ToString());
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static void WriteResults(Utf8JsonWriter w, AnalysisRun run)
    {
        w.WriteStartArray("results");

        foreach (var d in run.AllDiagnostics)
        {
            w.WriteStartObject();
            w.WriteString("ruleId", d.RuleId);
            w.WriteString("level", Level(d.Severity));

            w.WriteStartObject("message");
            w.WriteString("text", d.Message);
            w.WriteEndObject();

            w.WriteStartArray("locations");
            WriteLocation(w, d.Location);
            w.WriteEndArray();

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteLocation(Utf8JsonWriter w, SourceLocation location)
    {
        w.WriteStartObject();

        w.WriteStartObject("physicalLocation");
        w.WriteStartObject("artifactLocation");
        w.WriteString("uri", Uri(location.ArtifactPath));
        w.WriteEndObject();

        // SARIF exige startLine >= 1. Quando não temos linha, omitir a região é
        // mais honesto que apontar para a linha 1.
        if (location.Line > 0)
        {
            w.WriteStartObject("region");
            w.WriteNumber("startLine", location.Line);
            if (location.Column > 0)
                w.WriteNumber("startColumn", location.Column);
            w.WriteEndObject();
        }

        w.WriteEndObject();

        var logicos = new List<string>();
        if (!string.IsNullOrEmpty(location.EntryPath))
            logicos.Add(location.EntryPath);
        if (!string.IsNullOrEmpty(location.Symbol))
            logicos.Add(location.Symbol!);

        if (logicos.Count > 0)
        {
            w.WriteStartArray("logicalLocations");
            foreach (var nome in logicos)
            {
                w.WriteStartObject();
                w.WriteString("fullyQualifiedName", nome);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        w.WriteEndObject();
    }

    /// <summary>URI de SARIF usa barra normal, venha de onde vier o caminho.</summary>
    private static string Uri(string path) => path.Replace('\\', '/');

    private static string Level(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "note",
    };
}
