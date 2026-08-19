using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class CountRowsComparisonRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new CountRowsComparisonRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsGreaterThanZero()
    {
        var d = Assert.Single(Run("If(CountRows(colItens) > 0, Notify(\"tem\"))").Diagnostics);

        Assert.Equal("PF117", d.RuleId);
        Assert.Contains("IsEmpty", d.Message);
    }

    [Fact]
    public void ReportsGreaterOrEqualOne()
    {
        Assert.Single(Run("If(CountRows(colItens) >= 1, Notify(\"tem\"))").Diagnostics);
    }

    [Fact]
    public void ReportsWhenCountComesSecond()
    {
        Assert.Single(Run("If(0 < CountRows(colItens), Notify(\"tem\"))").Diagnostics);
    }

    [Fact]
    public void AcceptsComparisonWithOtherNumbers() =>
        Assert.Empty(Run("If(CountRows(colItens) > 5, Notify(\"muitos\"))").Diagnostics);

    [Fact]
    public void AcceptsEqualityWithZero()
    {
        // CountRows(x) = 0 pede IsEmpty sem negação; fora do escopo desta regra
        // para não sugerir a troca errada.
        Assert.Empty(Run("If(CountRows(colItens) = 0, Notify(\"vazio\"))").Diagnostics);
    }

    [Fact]
    public void AcceptsCountRowsUsedAsValue() =>
        Assert.Empty(Run("Set(varTotal, CountRows(colItens))").Diagnostics);

    [Fact]
    public void EvaluatesEveryCountRowsComparison()
    {
        var result = Run("If(CountRows(colA) > 0, 1, 2); If(CountRows(colB) > 5, 1, 2)");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
