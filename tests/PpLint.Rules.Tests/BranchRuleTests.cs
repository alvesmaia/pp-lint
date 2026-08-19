using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class BranchRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void PF113_ReportsIdenticalBranches()
    {
        var d = Assert.Single(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA + 1, varA + 1))").Diagnostics);

        Assert.Equal("PF113", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void PF113_IgnoresFormattingDifferences()
    {
        Assert.Single(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA+1, varA + 1))").Diagnostics);
    }

    [Fact]
    public void PF113_AcceptsDifferentBranches() =>
        Assert.Empty(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA, varB))").Diagnostics);

    [Fact]
    public void PF113_AcceptsTwoArgumentIf() =>
        Assert.Empty(Run(new IdenticalBranchesRule(), "If(varCond, Notify(\"oi\"))").Diagnostics);

    [Fact]
    public void PF113_EvaluatesEveryThreeArgumentIf()
    {
        var result = Run(new IdenticalBranchesRule(), "Set(varA, If(varX, 1, 1)); Set(varB, If(varY, 1, 2))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void PF114_ReportsRepeatedConditionInElse()
    {
        var d = Assert.Single(Run(new UnreachableBranchRule(),
            "Set(varX, If(varCond, 1, If(varCond, 2, 3)))").Diagnostics);

        Assert.Equal("PF114", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("varCond", d.Message);
    }

    [Fact]
    public void PF114_AcceptsDifferentConditions() =>
        Assert.Empty(Run(new UnreachableBranchRule(),
            "Set(varX, If(varA, 1, If(varB, 2, 3)))").Diagnostics);

    [Fact]
    public void PF114_AcceptsRepeatedConditionInThenBranch()
    {
        // If(c, If(c, ...), ...) é redundante mas alcançável; não é esta regra.
        Assert.Empty(Run(new UnreachableBranchRule(),
            "Set(varX, If(varCond, If(varCond, 1, 2), 3))").Diagnostics);
    }

    [Fact]
    public void PF114_EvaluatesEveryNestedElse()
    {
        var result = Run(new UnreachableBranchRule(),
            "Set(varA, If(varX, 1, If(varX, 2, 3))); Set(varB, If(varY, 1, If(varZ, 2, 3)))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
