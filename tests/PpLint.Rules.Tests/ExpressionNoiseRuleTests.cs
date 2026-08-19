using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class ExpressionNoiseRuleTests
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
    public void PF115_ReportsNotOfNot()
    {
        var d = Assert.Single(Run(new DoubleNegationRule(), "Set(varX, Not(Not(varAtivo)))").Diagnostics);

        Assert.Equal("PF115", d.RuleId);
        Assert.Contains("varAtivo", d.Message);
    }

    [Fact]
    public void PF115_ReportsBangBang()
    {
        Assert.Single(Run(new DoubleNegationRule(), "Set(varX, !!varAtivo)").Diagnostics);
    }

    [Fact]
    public void PF115_AcceptsSingleNegation() =>
        Assert.Empty(Run(new DoubleNegationRule(), "Set(varX, Not(varAtivo))").Diagnostics);

    [Fact]
    public void PF115_TripleNegationRespectsTheIndexInvariant()
    {
        var result = Run(new DoubleNegationRule(), "Set(varX, Not(Not(Not(varAtivo))))");

        Assert.All(result.Tallies, t => Assert.True(t.Violations <= t.Evaluated));
    }

    [Fact]
    public void PF116_ReportsFilterWithTrue()
    {
        var d = Assert.Single(Run(new PointlessFilterRule(), "ClearCollect(colX, Filter(Pedidos, true))").Diagnostics);

        Assert.Equal("PF116", d.RuleId);
        Assert.Contains("Pedidos", d.Message);
    }

    [Fact]
    public void PF116_AcceptsRealFilter() =>
        Assert.Empty(Run(new PointlessFilterRule(), "ClearCollect(colX, Filter(Pedidos, Total > 10))").Diagnostics);

    [Fact]
    public void PF116_IgnoresFilterWithFalse()
    {
        // Filter(fonte, false) devolve vazio: é suspeito, mas não é "filtro sem
        // efeito" — e sugerir remover o filtro seria conselho errado.
        Assert.Empty(Run(new PointlessFilterRule(), "ClearCollect(colX, Filter(Pedidos, false))").Diagnostics);
    }

    [Fact]
    public void PF118_ReportsConcatenationWithEmptyText()
    {
        var d = Assert.Single(Run(new EmptyConcatenationRule(), "Set(varX, varNome & \"\")").Diagnostics);

        Assert.Equal("PF118", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void PF118_ReportsWhenEmptyComesFirst()
    {
        Assert.Single(Run(new EmptyConcatenationRule(), "Set(varX, \"\" & varNome)").Diagnostics);
    }

    [Fact]
    public void PF118_AcceptsRealConcatenation() =>
        Assert.Empty(Run(new EmptyConcatenationRule(), "Set(varX, varNome & \" \" & varSobrenome)").Diagnostics);

    [Fact]
    public void PF118_EvaluatesEveryConcatenation()
    {
        var result = Run(new EmptyConcatenationRule(), "Set(varA, varX & \"\"); Set(varB, varY & \"z\")");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
