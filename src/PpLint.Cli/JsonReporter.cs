using System.Text.Encodings.Web;
using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;

namespace PpLint.Cli;

/// <summary>
/// Saída em JSON, para quem automatiza em cima do linter.
///
/// Escrito campo a campo com Utf8JsonWriter em vez de serializado por reflexão:
/// este formato é contrato com quem consome, e renomear uma propriedade C# não
/// pode mudá-lo em silêncio.
/// </summary>
public static class JsonReporter
{
    /// <summary>
    /// Sobe quando o formato mudar de um jeito que quebre quem já consome. Sem
    /// isto, a primeira mudança passaria despercebida do outro lado.
    /// </summary>
    private const int SchemaVersion = 1;

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        // O escape padrão transformaria "padrão" em "padrão". Em português
        // isso atinge quase toda mensagem e torna o arquivo ilegível.
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
            w.WriteNumber("schemaVersion", SchemaVersion);

            w.WriteStartObject("tool");
            w.WriteString("name", "pp-lint");
            w.WriteString("version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
            w.WriteEndObject();

            WriteSummary(w, run);

            w.WritePropertyName("compliance");
            WriteCompliance(w, run.Compliance);

            w.WriteStartArray("artifacts");
            foreach (var artifact in run.Artifacts)
                WriteArtifact(w, artifact);
            w.WriteEndArray();

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSummary(Utf8JsonWriter w, AnalysisRun run)
    {
        w.WriteStartObject("summary");
        w.WriteNumber("errors", run.AllDiagnostics.Count(d => d.Severity == Severity.Error));
        w.WriteNumber("warnings", run.AllDiagnostics.Count(d => d.Severity == Severity.Warning));
        w.WriteNumber("infos", run.AllDiagnostics.Count(d => d.Severity == Severity.Info));
        w.WriteNumber("durationSeconds", Math.Round(run.Elapsed.TotalSeconds, 3));
        w.WriteEndObject();
    }

    private static void WriteArtifact(Utf8JsonWriter w, ArtifactAnalysis artifact)
    {
        w.WriteStartObject();
        w.WriteString("path", artifact.Path);

        w.WritePropertyName("compliance");
        WriteCompliance(w, artifact.Compliance);

        w.WriteStartArray("diagnostics");
        foreach (var d in artifact.Diagnostics)
            WriteDiagnostic(w, d);
        w.WriteEndArray();

        w.WriteEndObject();
    }

    private static void WriteDiagnostic(Utf8JsonWriter w, Diagnostic d)
    {
        w.WriteStartObject();
        w.WriteString("ruleId", d.RuleId);
        w.WriteString("category", d.Category.ToString());
        w.WriteString("severity", SeverityText(d.Severity));
        w.WriteString("message", d.Message);

        w.WriteStartObject("location");
        w.WriteString("artifact", d.Location.ArtifactPath);
        w.WriteString("entry", d.Location.EntryPath);
        if (d.Location.Symbol is null)
            w.WriteNull("symbol");
        else
            w.WriteString("symbol", d.Location.Symbol);
        w.WriteNumber("line", d.Location.Line);
        w.WriteNumber("column", d.Location.Column);
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static void WriteCompliance(Utf8JsonWriter w, ComplianceReport report)
    {
        w.WriteStartObject();
        WriteScoreFields(w, report.Overall);

        w.WriteStartObject("byCategory");
        foreach (var (category, score) in report.ByCategory.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
        {
            w.WriteStartObject(category.ToString());
            WriteScoreFields(w, score);
            w.WriteEndObject();
        }
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static void WriteScoreFields(Utf8JsonWriter w, ComplianceScore score)
    {
        w.WriteNumber("percent", Math.Round(score.Percent, 1));
        w.WriteNumber("evaluatedTargets", score.EvaluatedTargets);
        w.WriteNumber("violations", score.Violations);
    }

    private static string SeverityText(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "info",
    };
}
