using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;
using PpLint.Core.Reporting;

namespace PpLint.Cli.Tests;

public class TextReporterTests
{
    private static Diagnostic Diag(string ruleId, Severity severity, string symbol, string message) =>
        new(ruleId, RuleCategory.Naming, severity, message,
            new SourceLocation("MinhaSolucao.zip", "CanvasApps/App.msapp", symbol, 0, 0));

    private static IReadOnlyList<RuleTally> Tallies() =>
        [
            new RuleTally("NM010", RuleCategory.Naming, Severity.Error, 100, 2),
            new RuleTally("PF101", RuleCategory.PowerFx, Severity.Warning, 50, 0),
        ];

    /// <summary>Uma execução de um artefato só, que é o caso comum.</summary>
    private static AnalysisRun RunOf(IReadOnlyList<Diagnostic> diagnostics, double segundos) =>
        AnalysisRun.From(
            [("a.msapp", new LintResult(diagnostics, Tallies()))],
            TimeSpan.FromSeconds(segundos));

    private static string Render(params Diagnostic[] diagnostics) =>
        TextReporter.Render(RunOf(diagnostics, 1.2), useColor: false, quiet: false);

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
            RunOf([Diag("NM010", Severity.Error, "Screen1", "mensagem detalhada")], 1),
            useColor: false, quiet: true);

        Assert.DoesNotContain("mensagem detalhada", output);
        Assert.Contains("Conformidade geral", output);
    }

    [Fact]
    public void Render_NoDiagnosticsShowsCleanMessage()
    {
        var output = TextReporter.Render(RunOf([], 1), useColor: false, quiet: false);
        Assert.Contains("Nenhum achado", output);
    }

    [Fact]
    public void Render_WithColorEmitsAnsiCodes()
    {
        var output = TextReporter.Render(
            RunOf([Diag("NM010", Severity.Error, "A", "a")], 1),
            useColor: true, quiet: false);

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
public class PerArtifactComplianceTests
{
    private static SourceLocation Loc(string artifact) => new(artifact, "e.json", "S", 0, 0);

    private static LintResult Result(string artifact, int evaluated, int violations)
    {
        var diagnostics = Enumerable.Range(0, violations)
            .Select(_ => new Diagnostic(
                "NM010", RuleCategory.Naming, Severity.Warning, "nome padrão", Loc(artifact)))
            .ToList();

        return new LintResult(
            diagnostics,
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, evaluated, violations)]);
    }

    private static string Render(params (string Path, LintResult Result)[] results) =>
        TextReporter.Render(AnalysisRun.From(results, TimeSpan.FromSeconds(1)), useColor: false, quiet: true);

    [Fact]
    public void SingleArtifactShowsOnlyTheOverallScore()
    {
        // Com um artefato só, o índice dele e o geral são o mesmo número.
        // Imprimir os dois seria repetir a mesma informação em duas linhas.
        var texto = Render(("a.msapp", Result("a.msapp", 10, 1)));

        Assert.Contains("Conformidade geral", texto);
        Assert.DoesNotContain("Por artefato", texto);
    }

    [Fact]
    public void SeveralArtifactsGetOneLineEach()
    {
        var texto = Render(
            ("a.msapp", Result("a.msapp", 10, 1)),
            ("b.msapp", Result("b.msapp", 10, 9)));

        Assert.Contains("Por artefato", texto);
        Assert.Contains("a.msapp", texto);
        Assert.Contains("b.msapp", texto);
        Assert.Contains("90,0%", texto);
        Assert.Contains("10,0%", texto);
    }

    [Fact]
    public void OverallStillAppearsWithSeveralArtifacts()
    {
        var texto = Render(
            ("a.msapp", Result("a.msapp", 10, 1)),
            ("b.msapp", Result("b.msapp", 10, 9)));

        Assert.Contains("Conformidade geral", texto);
    }
}

public class PerArtifactSpacingTests
{
    private static SourceLocation Loc(string a) => new(a, "e.json", "S", 0, 0);

    private static LintResult Result(string artifact) =>
        new([], [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, 10, 1)]);

    [Fact]
    public void PerArtifactBlockIsSeparatedFromTheOverallScore()
    {
        // Sem a linha em branco, o último artefato cola em "Conformidade geral"
        // e os dois blocos viram um só aos olhos de quem lê.
        var texto = TextReporter.Render(
            AnalysisRun.From(
                [("a.msapp", Result("a.msapp")), ("b.msapp", Result("b.msapp"))],
                TimeSpan.Zero),
            useColor: false, quiet: true);

        var linhas = texto.Replace("\r\n", "\n").Split('\n');
        var geral = Array.FindIndex(linhas, l => l.Contains("Conformidade geral"));

        Assert.True(geral > 0, "não achei a linha da conformidade geral");
        Assert.Equal(string.Empty, linhas[geral - 1].Trim());
    }
}
