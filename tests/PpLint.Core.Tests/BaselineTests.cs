using PpLint.Core;
using PpLint.Core.Baseline;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;

namespace PpLint.Core.Tests;

public class BaselineTests
{
    private static Diagnostic Diag(string ruleId, string entry, string? symbol, string mensagem = "achado") =>
        new(ruleId, RuleCategory.Naming, Severity.Warning, mensagem,
            new SourceLocation("a.msapp", entry, symbol, 0, 0));

    private static AnalysisRun Run(params Diagnostic[] diagnostics) =>
        AnalysisRun.From(
            [("a.msapp", new LintResult(
                diagnostics,
                [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, 100, diagnostics.Length)]))],
            TimeSpan.Zero);

    [Fact]
    public void EmptyBaselineHidesNothing()
    {
        var achados = new[] { Diag("NM010", "Controls/1.json", "btnA") };

        Assert.Equal(achados, PpLint.Core.Baseline.Baseline.Empty.Unbaselined(achados));
    }

    [Fact]
    public void KnownViolationIsHidden()
    {
        var achados = new[] { Diag("NM010", "Controls/1.json", "btnA") };
        var baseline = PpLint.Core.Baseline.Baseline.From(Run(achados));

        Assert.Empty(baseline.Unbaselined(achados));
    }

    [Fact]
    public void NewViolationSurvives()
    {
        var antes = new[] { Diag("NM010", "Controls/1.json", "btnA") };
        var baseline = PpLint.Core.Baseline.Baseline.From(Run(antes));

        var depois = new[]
        {
            Diag("NM010", "Controls/1.json", "btnA"),
            Diag("NM010", "Controls/1.json", "btnNovo"),
        };

        var novo = Assert.Single(baseline.Unbaselined(depois));
        Assert.Contains("btnNovo", novo.Location.Symbol);
    }

    [Fact]
    public void AnExtraCopyOfTheSameViolationIsNew()
    {
        // Três registradas e quatro encontradas: a quarta é nova. Sem contagem,
        // bastaria acrescentar um quinto problema idêntico ao lado de quatro já
        // perdoados para ele passar despercebido.
        var tres = Enumerable.Range(0, 3).Select(_ => Diag("NM010", "Controls/1.json", "gal")).ToArray();
        var baseline = PpLint.Core.Baseline.Baseline.From(Run(tres));

        var quatro = Enumerable.Range(0, 4).Select(_ => Diag("NM010", "Controls/1.json", "gal")).ToArray();

        Assert.Single(baseline.Unbaselined(quatro));
    }

    [Fact]
    public void FewerViolationsThanBaselineIsFine()
    {
        var tres = Enumerable.Range(0, 3).Select(_ => Diag("NM010", "Controls/1.json", "gal")).ToArray();
        var baseline = PpLint.Core.Baseline.Baseline.From(Run(tres));

        Assert.Empty(baseline.Unbaselined([Diag("NM010", "Controls/1.json", "gal")]));
    }

    [Fact]
    public void ChangingTheMessageDoesNotResurrectTheViolation()
    {
        // Melhorar o texto de uma regra não pode fazer centenas de achados já
        // revisados voltarem como novos — por isso a assinatura ignora a mensagem.
        var baseline = PpLint.Core.Baseline.Baseline.From(
            Run(Diag("NM010", "Controls/1.json", "btnA", "texto antigo")));

        Assert.Empty(baseline.Unbaselined([Diag("NM010", "Controls/1.json", "btnA", "texto novo e melhor")]));
    }

    [Fact]
    public void ADifferentRuleAtTheSamePlaceIsNew()
    {
        var baseline = PpLint.Core.Baseline.Baseline.From(Run(Diag("NM010", "Controls/1.json", "btnA")));

        Assert.Single(baseline.Unbaselined([Diag("NM011", "Controls/1.json", "btnA")]));
    }

    // ---- ida e volta pelo arquivo ----

    [Fact]
    public void SurvivesTheRoundTrip()
    {
        var original = PpLint.Core.Baseline.Baseline.From(Run(
            Diag("NM010", "Controls/1.json", "btnA"),
            Diag("NM011", "Controls/2.json", null),
            Diag("NM010", "Controls/1.json", "btnA")));

        var lida = PpLint.Core.Baseline.Baseline.Parse(original.ToJson());

        Assert.Equal(original.Total, lida.Total);
        Assert.Equal(
            original.Entries.Select(e => $"{e.RuleId}|{e.Entry}|{e.Symbol}|{e.Count}"),
            lida.Entries.Select(e => $"{e.RuleId}|{e.Entry}|{e.Symbol}|{e.Count}"));
    }

    [Fact]
    public void EntriesAreOrderedSoTheFileDiffsCleanly()
    {
        // O arquivo entra no repositório e é revisado em pull request. Ordem
        // instável faria cada regeneração produzir um diff ilegível.
        var baseline = PpLint.Core.Baseline.Baseline.From(Run(
            Diag("PF101", "Controls/9.json", "z"),
            Diag("NM010", "Controls/1.json", "b"),
            Diag("NM010", "Controls/1.json", "a")));

        Assert.Equal(
            ["NM010", "NM010", "PF101"],
            baseline.Entries.Select(e => e.RuleId));
        Assert.Equal(["a", "b", "z"], baseline.Entries.Select(e => e.Symbol));
    }

    [Fact]
    public void AccentsStayReadableInTheFile()
    {
        var json = PpLint.Core.Baseline.Baseline.From(
            Run(Diag("NM040", "Controls/1.json", "lblDescrição"))).ToJson();

        Assert.Contains("lblDescrição", json);
    }

    // ---- arquivo inválido ----

    [Fact]
    public void MalformedJsonIsRejectedWithAReadableMessage()
    {
        var ex = Assert.Throws<BaselineException>(() => PpLint.Core.Baseline.Baseline.Parse("{ nao é json"));

        Assert.Contains("JSON", ex.Message);
    }

    [Fact]
    public void ABaselineFromAnotherSchemaVersionIsRejected()
    {
        // Ler em silêncio uma linha de base de outro formato esconderia achados
        // sem ninguém saber.
        var ex = Assert.Throws<BaselineException>(() =>
            PpLint.Core.Baseline.Baseline.Parse("""{ "schemaVersion": 99, "entries": [] }"""));

        Assert.Contains("pp-lint baseline", ex.Message);
    }

    [Fact]
    public void AFileWithoutEntriesIsRejected() =>
        Assert.Throws<BaselineException>(() =>
            PpLint.Core.Baseline.Baseline.Parse("""{ "schemaVersion": 1 }"""));
}
