using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class BooleanRedundancyRuleTests
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
    public void PF111_ReportsIfTrueFalse()
    {
        var d = Assert.Single(Run(new RedundantBooleanIfRule(), "Set(varX, If(IsBlank(varY), true, false))").Diagnostics);

        Assert.Equal("PF111", d.RuleId);
        Assert.Contains("IsBlank", d.Message);
    }

    [Fact]
    public void PF111_ReportsIfFalseTrueSuggestingNot()
    {
        var d = Assert.Single(Run(new RedundantBooleanIfRule(), "Set(varX, If(varCond, false, true))").Diagnostics);

        Assert.Contains("Not(", d.Message);
    }

    [Fact]
    public void PF111_AcceptsIfWithRealBranches() =>
        Assert.Empty(Run(new RedundantBooleanIfRule(), "Set(varX, If(varCond, 1, 2))").Diagnostics);

    [Fact]
    public void PF111_AcceptsIfWithOnlyOneBooleanBranch() =>
        Assert.Empty(Run(new RedundantBooleanIfRule(), "Set(varX, If(varCond, true, varOutro))").Diagnostics);

    [Fact]
    public void PF111_AcceptsTwoArgumentIf() =>
        Assert.Empty(Run(new RedundantBooleanIfRule(), "If(varCond, Notify(\"oi\"))").Diagnostics);

    [Fact]
    public void PF111_EvaluatesEveryIf()
    {
        var result = Run(new RedundantBooleanIfRule(), "Set(varA, If(varX, true, false)); Set(varB, If(varY, 1, 2))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void PF112_ReportsComparisonWithTrue()
    {
        var d = Assert.Single(Run(new BooleanComparisonRule(), "If(varAtivo = true, Notify(\"oi\"))").Diagnostics);

        Assert.Equal("PF112", d.RuleId);
        Assert.Contains("varAtivo", d.Message);
    }

    [Fact]
    public void PF112_ReportsComparisonWithFalseSuggestingNot()
    {
        var d = Assert.Single(Run(new BooleanComparisonRule(), "If(varAtivo = false, Notify(\"oi\"))").Diagnostics);

        Assert.Contains("Not(", d.Message);
    }

    [Fact]
    public void PF112_ReportsInequalityWithTrue()
    {
        Assert.Single(Run(new BooleanComparisonRule(), "If(varAtivo <> true, Notify(\"oi\"))").Diagnostics);
    }

    [Fact]
    public void PF112_ReportsWhenLiteralComesFirst()
    {
        Assert.Single(Run(new BooleanComparisonRule(), "If(true = varAtivo, Notify(\"oi\"))").Diagnostics);
    }

    [Fact]
    public void PF112_AcceptsComparisonBetweenValues() =>
        Assert.Empty(Run(new BooleanComparisonRule(), "If(varTotal = 10, Notify(\"oi\"))").Diagnostics);

    [Fact]
    public void PF112_IgnoresTwoLiterals()
    {
        // true = true é condição constante: assunto da PF110, não desta regra.
        Assert.Empty(Run(new BooleanComparisonRule(), "If(true = true, Notify(\"oi\"))").Diagnostics);
    }
}
