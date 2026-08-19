using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Core.Suppression;

namespace PpLint.Core.Tests;

[Rule("PF101", RuleCategory.PowerFx, Severity.Warning)]
public sealed class FakeUnusedVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
            foreach (var control in app.AllControls())
                foreach (var property in control.Properties)
                {
                    ctx.Evaluated(1);
                    ctx.Report(property.Location, "achado de teste");
                }
    }
}

public class SuppressionEngineTests
{
    private static SourceLocation Loc(string? symbol) => new("app.msapp", "Controls/1.json", symbol, 0, 0);

    private static PowerPlatformProject ProjectWith(params (string Property, string Script)[] properties)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        foreach (var (property, script) in properties)
            button.Properties.Add(new PowerFxProperty(property, script, Loc($"btnOk.{property}")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "app.msapp" };
        project.Apps.Add(app);
        return project;
    }

    [Fact]
    public void SuppressedFindingIsNotReported()
    {
        var project = ProjectWith(("OnSelect", "// pp-lint: disable=PF101"));
        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SuppressedFindingLeavesNumeratorAndDenominator()
    {
        var project = ProjectWith(("OnSelect", "// pp-lint: disable=PF101"));
        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(0, tally.Violations);
        Assert.Equal(0, tally.Evaluated);
    }

    [Fact]
    public void SuppressionDoesNotInflateCompliance()
    {
        // Duas propriedades, uma suprimida: a conformidade tem de ser 0%,
        // não 50%, porque o alvo suprimido some da conta inteira.
        var project = ProjectWith(
            ("OnSelect", "// pp-lint: disable=PF101"),
            ("OnChange", "Set(varX, 1)"));

        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(1, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void UnsuppressedFindingSurvives()
    {
        var project = ProjectWith(("OnSelect", "Set(varX, 1)"));
        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        Assert.Single(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void DirectiveForAnotherRuleDoesNotSuppress()
    {
        var project = ProjectWith(("OnSelect", "// pp-lint: disable=NM011"));
        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void RunWithoutIndexKeepsPhaseOneBehaviour()
    {
        var project = ProjectWith(("OnSelect", "// pp-lint: disable=PF101"));
        var result = new RuleEngine([new FakeUnusedVariableRule()]).Run(project, PpLintConfig.Default);

        Assert.Single(result.Diagnostics);
    }
}
