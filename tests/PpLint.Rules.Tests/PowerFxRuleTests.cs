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
