using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;

namespace PpLint.Cli.Tests;

public class HtmlReporterTests
{
    private static SourceLocation Loc(string artefato, string? simbolo = "btnA") =>
        new(artefato, "Controls/1.json", simbolo, 0, 0);

    private static LintResult Result(string artefato, params (Severity Sev, string Msg)[] achados) =>
        new(
            achados.Select(a => new Diagnostic("NM010", RuleCategory.Naming, a.Sev, a.Msg, Loc(artefato))).ToList(),
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, 100, achados.Length)]);

    private static string Render(params (string Path, LintResult Result)[] resultados) =>
        HtmlReporter.Render(AnalysisRun.From(resultados, TimeSpan.FromSeconds(1)));

    [Fact]
    public void ProducesAWellFormedDocument()
    {
        var html = Render(("a.msapp", Result("a.msapp", (Severity.Warning, "nome padrão"))));

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<html lang=\"pt-BR\">", html);
        Assert.EndsWith("</html>\r\n", html.Replace("\n", "\r\n").Replace("\r\r", "\r"));
    }

    [Fact]
    public void IsSelfContained()
    {
        // O arquivo é aberto de um anexo ou de uma pasta de rede, muitas vezes
        // sem internet. Depender de CDN o faria chegar quebrado justamente na
        // reunião em que seria usado.
        var html = Render(("a.msapp", Result("a.msapp", (Severity.Error, "erro"))));

        Assert.DoesNotContain("<script src=", html);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
        Assert.Contains("<style>", html);
    }

    [Fact]
    public void EscapesMessagesThatContainMarkup()
    {
        // A PF118 chega a citar trechos de SVG. Sem escape, o relatório sai
        // quebrado — ou executa o que veio do artefato.
        var html = Render(("a.msapp", Result("a.msapp",
            (Severity.Info, "A fórmula '\"<path fill=\'none\'>\" & varX' concatena com vazio"))));

        Assert.DoesNotContain("<path fill=", html);
        Assert.Contains("&lt;path", html);
    }

    [Fact]
    public void EscapesAmpersandsFromPowerFxConcatenation()
    {
        var html = Render(("a.msapp", Result("a.msapp", (Severity.Info, "varNome & \" \" & varSobrenome"))));

        Assert.Contains("&amp;", html);
    }

    [Fact]
    public void ShowsTheComplianceIndex()
    {
        var html = Render(("a.msapp", Result("a.msapp", (Severity.Warning, "x"))));

        Assert.Contains("class=\"numero", html);
        Assert.Contains("verificações passaram", html);
    }

    [Fact]
    public void PerArtifactTableAppearsOnlyWithSeveralArtifacts()
    {
        var um = Render(("a.msapp", Result("a.msapp", (Severity.Warning, "x"))));
        var dois = Render(
            ("a.msapp", Result("a.msapp", (Severity.Warning, "x"))),
            ("b.msapp", Result("b.msapp", (Severity.Warning, "y"))));

        Assert.DoesNotContain("Por artefato", um);
        Assert.Contains("Por artefato", dois);
    }

    [Fact]
    public void ArtifactsAreOrderedWorstFirst()
    {
        // Quem abre o relatório quer saber por onde começar.
        var bom = new LintResult(
            [new Diagnostic("NM010", RuleCategory.Naming, Severity.Warning, "x", Loc("bom.msapp"))],
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, 100, 1)]);
        var ruim = new LintResult(
            [new Diagnostic("NM010", RuleCategory.Naming, Severity.Warning, "y", Loc("ruim.msapp"))],
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, 2, 1)]);

        var html = Render(("bom.msapp", bom), ("ruim.msapp", ruim));
        var secao = html[html.IndexOf("Por artefato", StringComparison.Ordinal)..];

        Assert.True(
            secao.IndexOf("ruim.msapp", StringComparison.Ordinal) < secao.IndexOf("bom.msapp", StringComparison.Ordinal),
            "o artefato pior precisa aparecer primeiro");
    }

    [Fact]
    public void EachFindingLinksToItsDocumentation()
    {
        var html = Render(("a.msapp", Result("a.msapp", (Severity.Warning, "x"))));

        Assert.Contains("docs/rules/NM010.md", html);
    }

    [Fact]
    public void FindingsCarryTheSeverityForFiltering()
    {
        var html = Render(("a.msapp", Result("a.msapp",
            (Severity.Error, "e"), (Severity.Warning, "a"), (Severity.Info, "i"))));

        Assert.Contains("data-sev=\"erro\"", html);
        Assert.Contains("data-sev=\"aviso\"", html);
        Assert.Contains("data-sev=\"info\"", html);
    }

    [Fact]
    public void CleanRunSaysSo()
    {
        var vazio = new LintResult([], [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, 10, 0)]);

        Assert.Contains("Nenhum achado", Render(("a.msapp", vazio)));
    }

    [Fact]
    public void AdaptsToTheDarkTheme()
    {
        var html = Render(("a.msapp", Result("a.msapp", (Severity.Warning, "x"))));

        Assert.Contains("prefers-color-scheme: dark", html);
        Assert.Contains("color-scheme: light dark", html);
    }

    [Fact]
    public void NumbersUseBrazilianFormat()
    {
        var html = Render(("a.msapp", Result("a.msapp", (Severity.Warning, "x"))));

        Assert.Contains("99,0%", html);
    }
}
