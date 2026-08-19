using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;
using PpLint.Core.Suppression;

namespace PpLint.Core.Tests;

/// <summary>Reporta em toda propriedade cujo script contenha "ruim".</summary>
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
                    if (property.Script.Contains("ruim", StringComparison.Ordinal))
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

    private static LintResult Run(PowerPlatformProject project) =>
        new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

    [Fact]
    public void SuppressedFindingIsNotReported()
    {
        var project = ProjectWith(("OnSelect", "ruim // pp-lint: disable=PF101"));

        Assert.Empty(Run(project).Diagnostics);
    }

    [Fact]
    public void SuppressionDoesNotRaiseTheComplianceScore()
    {
        // Duas propriedades ruins em quatro: 50% de conformidade.
        // Silenciar uma delas não pode melhorar a nota — só tira o achado da lista.
        (string, string)[] properties =
        [
            ("A", "ruim"),
            ("B", "ruim"),
            ("C", "bom"),
            ("D", "bom"),
        ];

        var semSupressao = ComplianceScorer.Compute(Run(ProjectWith(properties)).Tallies);

        var comSupressao = ComplianceScorer.Compute(Run(ProjectWith(
        [
            ("A", "ruim // pp-lint: disable=PF101"),
            ("B", "ruim"),
            ("C", "bom"),
            ("D", "bom"),
        ])).Tallies);

        Assert.Equal(50.0, semSupressao.Overall.Percent, 4);
        Assert.Equal(
            semSupressao.Overall.Percent,
            comSupressao.Overall.Percent,
            4);
    }

    [Fact]
    public void SuppressingEverythingDoesNotProduceAPerfectScore()
    {
        // O cenário que o modo anterior permitia: silenciar tudo e exibir 100%.
        var project = ProjectWith(
            ("A", "ruim // pp-lint: disable=PF101"),
            ("B", "ruim // pp-lint: disable=PF101"));

        var result = Run(project);
        var compliance = ComplianceScorer.Compute(result.Tallies);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0.0, compliance.Overall.Percent, 4);
    }

    [Fact]
    public void SuppressedFindingStillCountsAsViolation()
    {
        var project = ProjectWith(("OnSelect", "ruim // pp-lint: disable=PF101"));

        var tally = Assert.Single(Run(project).Tallies);
        Assert.Equal(1, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void UnsuppressedFindingSurvives()
    {
        var project = ProjectWith(("OnSelect", "ruim"));

        Assert.Single(Run(project).Diagnostics);
    }

    [Fact]
    public void DirectiveForAnotherRuleDoesNotSuppress()
    {
        var project = ProjectWith(("OnSelect", "ruim // pp-lint: disable=NM011"));

        Assert.Single(Run(project).Diagnostics);
    }

    [Fact]
    public void RunWithoutIndexKeepsPhaseOneBehaviour()
    {
        var project = ProjectWith(("OnSelect", "ruim // pp-lint: disable=PF101"));
        var result = new RuleEngine([new FakeUnusedVariableRule()]).Run(project, PpLintConfig.Default);

        Assert.Single(result.Diagnostics);
    }
}
