using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;

namespace PpLint.Cli.Tests;

public class TextReporterTests
{
    private static Diagnostic Diag(string ruleId, Severity severity, string symbol, string message) =>
        new(ruleId, RuleCategory.Naming, severity, message,
            new SourceLocation("MinhaSolucao.zip", "CanvasApps/App.msapp", symbol, 0, 0));

    private static ComplianceReport Compliance() =>
        ComplianceScorer.Compute([
            new RuleTally("NM010", RuleCategory.Naming, Severity.Error, 100, 2),
            new RuleTally("PF101", RuleCategory.PowerFx, Severity.Warning, 50, 0),
        ]);

    private static string Render(params Diagnostic[] diagnostics) =>
        TextReporter.Render(diagnostics, Compliance(), TimeSpan.FromSeconds(1.2), useColor: false, quiet: false);

    [Fact]
    public void Render_ShowsRuleIdSeverityAndMessage()
    {
        var output = Render(Diag("NM010", Severity.Error, "Screen1", "Controle com nome padrão"));

        Assert.Contains("NM010", output);
        Assert.Contains("error", output);
        Assert.Contains("Controle com nome padrão", output);
        Assert.Contains("Screen1", output);
    }

    [Fact]
    public void Render_GroupsByArtifactPath()
    {
        var output = Render(
            Diag("NM010", Severity.Error, "Screen1", "a"),
            Diag("NM011", Severity.Warning, "Botao", "b"));

        Assert.Equal(1, CountOccurrences(output, "MinhaSolucao.zip"));
    }

    [Fact]
    public void Render_ShowsOverallCompliance()
    {
        var output = Render(Diag("NM010", Severity.Error, "Screen1", "a"));
        Assert.Contains("Conformidade geral", output);
    }

    [Fact]
    public void Render_ShowsCategoryBreakdownWithCounts()
    {
        var output = Render(Diag("NM010", Severity.Error, "Screen1", "a"));

        Assert.Contains("Nomenclatura", output);
        Assert.Contains("98/100", output);
        Assert.Contains("Power Fx", output);
    }

    [Fact]
    public void Render_ShowsSummaryCounts()
    {
        var output = Render(
            Diag("NM010", Severity.Error, "A", "a"),
            Diag("NM011", Severity.Warning, "B", "b"),
            Diag("NM011", Severity.Warning, "C", "c"));

        Assert.Contains("1 erro", output);
        Assert.Contains("2 avisos", output);
    }

    [Fact]
    public void Render_ShowsTopOffenders()
    {
        var output = Render(
            Diag("NM011", Severity.Warning, "A", "a"),
            Diag("NM011", Severity.Warning, "B", "b"),
            Diag("NM010", Severity.Error, "C", "c"));

        Assert.Contains("NM011 (2", output);
    }

    [Fact]
    public void Render_QuietOmitsIndividualDiagnostics()
    {
        var output = TextReporter.Render(
            [Diag("NM010", Severity.Error, "Screen1", "mensagem detalhada")],
            Compliance(), TimeSpan.FromSeconds(1), useColor: false, quiet: true);

        Assert.DoesNotContain("mensagem detalhada", output);
        Assert.Contains("Conformidade geral", output);
    }

    [Fact]
    public void Render_NoDiagnosticsShowsCleanMessage()
    {
        var output = TextReporter.Render([], Compliance(), TimeSpan.FromSeconds(1), useColor: false, quiet: false);
        Assert.Contains("Nenhum achado", output);
    }

    [Fact]
    public void Render_WithColorEmitsAnsiCodes()
    {
        var output = TextReporter.Render(
            [Diag("NM010", Severity.Error, "A", "a")],
            Compliance(), TimeSpan.FromSeconds(1), useColor: true, quiet: false);

        Assert.Contains("\u001b[", output);
    }

    [Fact]
    public void Render_WithoutColorEmitsNoAnsiCodes()
    {
        Assert.DoesNotContain("\u001b[", Render(Diag("NM010", Severity.Error, "A", "a")));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
