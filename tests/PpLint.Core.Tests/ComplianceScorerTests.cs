using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;

namespace PpLint.Core.Tests;

public class ComplianceScorerTests
{
    private static RuleTally Tally(string id, RuleCategory cat, Severity sev, int evaluated, int violations)
        => new(id, cat, sev, evaluated, violations);

    [Fact]
    public void Compute_NoViolationsIsHundredPercent()
    {
        var report = ComplianceScorer.Compute([Tally("A", RuleCategory.Naming, Severity.Warning, 100, 0)]);
        Assert.Equal(100.0, report.Overall.Percent, 4);
        Assert.Equal(100, report.Overall.EvaluatedTargets);
        Assert.Equal(0, report.Overall.Violations);
    }

    [Fact]
    public void Compute_AllTargetsViolatingIsZeroPercent()
    {
        var report = ComplianceScorer.Compute([Tally("A", RuleCategory.Naming, Severity.Warning, 10, 10)]);
        Assert.Equal(0.0, report.Overall.Percent, 4);
    }

    [Fact]
    public void Compute_SingleRuleUsesSimpleRatio()
    {
        // 46 de 358 reprovados; o peso é irrelevante quando há uma só regra.
        var report = ComplianceScorer.Compute([Tally("NM011", RuleCategory.Naming, Severity.Warning, 358, 46)]);
        Assert.Equal(100.0 * (1 - 46.0 / 358.0), report.Overall.Percent, 4);
    }

    [Fact]
    public void Compute_WeightsErrorsTenTimesMoreThanInfos()
    {
        // Error: 1 violação em 10 alvos -> 10*1 / 10*10
        // Info:  1 violação em 10 alvos ->  1*1 / 1*10
        // Total: (10 + 1) / (100 + 10) = 11/110 = 0,1
        var report = ComplianceScorer.Compute([
            Tally("E", RuleCategory.PowerFx, Severity.Error, 10, 1),
            Tally("I", RuleCategory.PowerFx, Severity.Info, 10, 1),
        ]);
        Assert.Equal(90.0, report.Overall.Percent, 4);
    }

    [Fact]
    public void Compute_ZeroEvaluatedIsHundredPercent()
    {
        var report = ComplianceScorer.Compute([Tally("A", RuleCategory.Flow, Severity.Error, 0, 0)]);
        Assert.Equal(100.0, report.Overall.Percent, 4);
    }

    [Fact]
    public void Compute_EmptyTalliesIsHundredPercent()
    {
        var report = ComplianceScorer.Compute([]);
        Assert.Equal(100.0, report.Overall.Percent, 4);
        Assert.Empty(report.ByCategory);
    }

    [Fact]
    public void Compute_GroupsByCategory()
    {
        var report = ComplianceScorer.Compute([
            Tally("NM1", RuleCategory.Naming, Severity.Warning, 10, 5),
            Tally("NM2", RuleCategory.Naming, Severity.Warning, 10, 0),
            Tally("PF1", RuleCategory.PowerFx, Severity.Warning, 20, 0),
        ]);

        Assert.Equal(2, report.ByCategory.Count);
        Assert.Equal(75.0, report.ByCategory[RuleCategory.Naming].Percent, 4);
        Assert.Equal(100.0, report.ByCategory[RuleCategory.PowerFx].Percent, 4);
        Assert.Equal(20, report.ByCategory[RuleCategory.Naming].EvaluatedTargets);
        Assert.Equal(5, report.ByCategory[RuleCategory.Naming].Violations);
    }

    [Fact]
    public void Compute_CategoryWithNoEvaluatedTargetsIsOmitted()
    {
        var report = ComplianceScorer.Compute([
            Tally("NM1", RuleCategory.Naming, Severity.Warning, 10, 0),
            Tally("SEC1", RuleCategory.Security, Severity.Error, 0, 0),
        ]);

        Assert.True(report.ByCategory.ContainsKey(RuleCategory.Naming));
        Assert.False(report.ByCategory.ContainsKey(RuleCategory.Security));
    }

    [Fact]
    public void Compute_ResultIsAlwaysWithinBounds()
    {
        var report = ComplianceScorer.Compute([
            Tally("A", RuleCategory.Naming, Severity.Error, 1, 1),
            Tally("B", RuleCategory.PowerFx, Severity.Info, 1, 0),
        ]);

        Assert.InRange(report.Overall.Percent, 0.0, 100.0);
    }
}
