using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;

namespace PpLint.Core.Tests;

public class AnalysisRunTests
{
    private static SourceLocation Loc(string artifact) => new(artifact, "e.json", "S", 0, 0);

    private static Diagnostic Diag(string artifact, string ruleId, Severity severity) =>
        new(ruleId, RuleCategory.Naming, severity, "mensagem", Loc(artifact));

    private static LintResult Result(string artifact, int evaluated, int violations)
    {
        var diagnostics = Enumerable.Range(0, violations)
            .Select(_ => Diag(artifact, "NM010", Severity.Warning))
            .ToList();

        return new LintResult(
            diagnostics,
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, evaluated, violations)]);
    }

    [Fact]
    public void EachArtifactKeepsItsOwnScore()
    {
        var run = AnalysisRun.From(
            [("a.msapp", Result("a.msapp", 10, 1)), ("b.msapp", Result("b.msapp", 10, 9))],
            TimeSpan.FromSeconds(1));

        Assert.Equal(2, run.Artifacts.Count);
        Assert.Equal(90.0, run.Artifacts[0].Compliance.Overall.Percent, 1);
        Assert.Equal(10.0, run.Artifacts[1].Compliance.Overall.Percent, 1);
    }

    [Fact]
    public void OverallIsComputedFromAllTalliesNotFromTheAverageOfArtifacts()
    {
        // Um artefato pequeno e ruim não pode pesar tanto quanto um grande e bom:
        // a média das partes daria 50%, mas 1 de 110 alvos falhou.
        var run = AnalysisRun.From(
            [("grande.msapp", Result("grande.msapp", 100, 0)), ("pequeno.msapp", Result("pequeno.msapp", 10, 1))],
            TimeSpan.Zero);

        Assert.Equal(110, run.Compliance.Overall.EvaluatedTargets);
        Assert.Equal(1, run.Compliance.Overall.Violations);
        Assert.True(
            run.Compliance.Overall.Percent > 99.0,
            $"esperava perto de 100%, veio {run.Compliance.Overall.Percent}");
    }

    [Fact]
    public void AllDiagnosticsPreservesArtifactOrder()
    {
        var run = AnalysisRun.From(
            [("a.msapp", Result("a.msapp", 1, 1)), ("b.msapp", Result("b.msapp", 1, 1))],
            TimeSpan.Zero);

        Assert.Equal(
            ["a.msapp", "b.msapp"],
            run.AllDiagnostics.Select(d => d.Location.ArtifactPath));
    }

    [Fact]
    public void EmptyRunIsFullyCompliant()
    {
        // Sem alvo avaliado não há como estar não-conforme; 0% assustaria à toa.
        var run = AnalysisRun.From([], TimeSpan.Zero);

        Assert.Empty(run.Artifacts);
        Assert.Equal(100.0, run.Compliance.Overall.Percent, 1);
    }

    [Fact]
    public void ElapsedIsCarried()
    {
        var run = AnalysisRun.From([], TimeSpan.FromMilliseconds(250));

        Assert.Equal(250, run.Elapsed.TotalMilliseconds, 1);
    }
}
