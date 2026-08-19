using PpLint.Core;
using PpLint.Rules.Fx;
using static PpLint.Rules.Tests.RuleTestHarness;

namespace PpLint.Rules.Tests;

public class UnusedGlobalVariableRuleTests
{
    [Fact]
    public void Reports_GlobalThatIsNeverRead()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Set(varTotal, 10)")))));

        var d = Assert.Single(Run(new UnusedGlobalVariableRule(), project).Diagnostics);
        Assert.Equal("PF101", d.RuleId);
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains("varTotal", d.Message);
    }

    [Fact]
    public void DoesNotReport_GlobalReadInAnotherControl()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Set(varTotal, 10)")),
            Ctl("lblTotal", "label", ("Text", "varTotal")))));

        Assert.Empty(Run(new UnusedGlobalVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void Evaluates_OneTargetPerGlobalVariable()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Set(varA, 1); Set(varB, 2)")),
            Ctl("lblA", "label", ("Text", "varA")))));

        var result = Run(new UnusedGlobalVariableRule(), project);
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void Evaluates_ZeroWhenAppHasNoGlobals()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Notify(\"oi\")")))));

        Assert.Equal(0, Assert.Single(Run(new UnusedGlobalVariableRule(), project).Tallies).Evaluated);
    }

    [Fact]
    public void ReportsOncePerVariableEvenWithTwoDefinitions()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Set(varTotal, 1)"), ("OnChange", "Set(varTotal, 2)")))));

        Assert.Single(Run(new UnusedGlobalVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void HandlesMultipleAppsIndependently()
    {
        var appA = App("A", Screen("scrA", Ctl("btnA", "button", ("OnSelect", "Set(varX, 1)"))));
        var appB = App("B", Screen("scrB",
            Ctl("btnB", "button", ("OnSelect", "Set(varX, 1)")),
            Ctl("lblB", "label", ("Text", "varX"))));

        // varX é lida no app B, mas não no app A: uma violação apenas.
        var result = Run(new UnusedGlobalVariableRule(), ProjectWith(appA, appB));
        Assert.Single(result.Diagnostics);
        Assert.Equal(2, Assert.Single(result.Tallies).Evaluated);
    }
}

public class ConstantConditionRuleTests
{
    private static PpLint.Core.Rules.LintResult Check(string script) =>
        Run(new ConstantConditionRule(),
            ProjectWith(App("A", Screen("scrHome", Ctl("btnOk", "button", ("OnSelect", script))))));

    [Fact]
    public void Reports_ComparisonBetweenNumericLiterals()
    {
        var d = Assert.Single(Check("If(2 > 1, Notify(\"sempre\"))").Diagnostics);
        Assert.Equal("PF110", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void Reports_EqualityBetweenNumericLiterals()
    {
        Assert.Single(Check("If(1 = 1, Notify(\"sempre\"))").Diagnostics);
    }

    [Fact]
    public void Reports_BooleanLiteralAsIfCondition()
    {
        Assert.Single(Check("If(true, Notify(\"sempre\"))").Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ConditionUsingAVariable()
    {
        Assert.Empty(Check("If(varTotal > 1, Notify(\"talvez\"))").Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ConditionUsingAFunctionCall()
    {
        Assert.Empty(Check("If(IsBlank(txtNome.Text), Notify(\"vazio\"))").Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ArithmeticBetweenLiterals()
    {
        // 1 + 2 é cálculo, não condição; só comparações constantes interessam.
        Assert.Empty(Check("Set(varTotal, 1 + 2)").Diagnostics);
    }

    [Fact]
    public void DoesNotReport_BooleanLiteralOutsideACondition()
    {
        Assert.Empty(Check("Set(varAtivo, true)").Diagnostics);
    }

    [Fact]
    public void Evaluates_EachConditionSeparately()
    {
        var result = Check("If(varA, 1, If(2 > 1, 2, 3))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void Evaluates_ZeroWhenThereAreNoConditions()
    {
        Assert.Equal(0, Assert.Single(Check("Notify(\"oi\")").Tallies).Evaluated);
    }

    [Fact]
    public void UnparseableExpressionIsIgnored()
    {
        Assert.Empty(Check("If(2 > 1,").Diagnostics);
    }
}
