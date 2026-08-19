using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class UnusedVariableRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static PowerPlatformProject ProjectWith(params (string Screen, string Property, string Script)[] formulas)
    {
        var app = new CanvasApp { Name = "App", Location = Loc(null) };

        foreach (var screenName in formulas.Select(f => f.Screen).Distinct())
        {
            var screen = new Control
            {
                Name = screenName,
                TemplateName = "screen",
                IsScreen = true,
                Location = Loc(screenName),
            };
            var button = new Control
            {
                Name = $"btn{screenName}",
                TemplateName = "button",
                Location = Loc($"btn{screenName}"),
            };

            foreach (var (_, property, script) in formulas.Where(f => f.Screen == screenName))
                button.Properties.Add(new PowerFxProperty(property, script, Loc($"btn{screenName}.{property}")));

            screen.AddChild(button);
            app.Screens.Add(screen);
        }

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);
        return project;
    }

    private static LintResult Run(IRule rule, PowerPlatformProject project) =>
        new RuleEngine([rule]).Run(project, PpLintConfig.Default);

    [Fact]
    public void PF102_ReportsContextVariableNeverRead()
    {
        var project = ProjectWith(("scrA", "OnSelect", "UpdateContext({locFiltro: 1})"));
        var d = Assert.Single(Run(new UnusedContextVariableRule(), project).Diagnostics);

        Assert.Equal("PF102", d.RuleId);
        Assert.Contains("locFiltro", d.Message);
    }

    [Fact]
    public void PF102_AcceptsReadOnTheSameScreen()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "UpdateContext({locFiltro: 1})"),
            ("scrA", "Text", "locFiltro"));

        Assert.Empty(Run(new UnusedContextVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void PF102_ReadOnAnotherScreenDoesNotCount()
    {
        // Variável de contexto vive numa tela só; ler o mesmo nome em outra
        // tela lê outra coisa, e a definição original continua sem uso.
        var project = ProjectWith(
            ("scrA", "OnSelect", "UpdateContext({locFiltro: 1})"),
            ("scrB", "Text", "locFiltro"));

        Assert.Single(Run(new UnusedContextVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void PF102_NavigateContextIsReadOnTheDestination()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "Navigate(scrB, Fade, {locId: 7})"),
            ("scrB", "Text", "locId"));

        Assert.Empty(Run(new UnusedContextVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void PF102_IgnoresGlobalsAndCollections()
    {
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, 1); ClearCollect(colY, [1])"));
        var result = Run(new UnusedContextVariableRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void PF103_ReportsCollectionNeverUsed()
    {
        var project = ProjectWith(("scrA", "OnSelect", "ClearCollect(colItens, [1])"));
        var d = Assert.Single(Run(new UnusedCollectionRule(), project).Diagnostics);

        Assert.Equal("PF103", d.RuleId);
        Assert.Contains("colItens", d.Message);
    }

    [Fact]
    public void PF103_AcceptsUseOnAnyScreen()
    {
        // Coleção é de app inteiro: usar em outra tela conta.
        var project = ProjectWith(
            ("scrA", "OnSelect", "ClearCollect(colItens, [1])"),
            ("scrB", "Items", "colItens"));

        Assert.Empty(Run(new UnusedCollectionRule(), project).Diagnostics);
    }

    [Fact]
    public void PF103_EvaluatesOneTargetPerCollection()
    {
        var project = ProjectWith(("scrA", "OnSelect", "ClearCollect(colA, [1]); ClearCollect(colB, [2])"));
        var result = Run(new UnusedCollectionRule(), project);

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(2, tally.Violations);
    }

    [Fact]
    public void PF103_EvaluatesZeroWhenThereAreNoCollections()
    {
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, 1)"));

        Assert.Equal(0, Assert.Single(Run(new UnusedCollectionRule(), project).Tallies).Evaluated);
    }
}

public class CollectionUsageDetectionTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(params (string Property, string Script)[] formulas)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        foreach (var (property, script) in formulas)
            button.Properties.Add(new PowerFxProperty(property, script, Loc($"btnA.{property}")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new UnusedCollectionRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void CollectionReadInsideFilterCountsAsUse()
    {
        // Uso mais comum de coleção: Filter(colItens, ...). Se isso não contar,
        // a regra acusa como órfã toda coleção realmente consumida.
        Assert.Empty(Run(
            ("OnSelect", "ClearCollect(colItens, [1])"),
            ("Items", "Filter(colItens, Value > 0)")).Diagnostics);
    }

    [Fact]
    public void CollectionReadInsideForAllCountsAsUse()
    {
        Assert.Empty(Run(
            ("OnSelect", "ClearCollect(colItens, [1])"),
            ("Text", "Concat(ForAll(colItens, Value), Value)")).Diagnostics);
    }

    [Fact]
    public void CollectionUsedInTheSameFormulaCountsAsUse()
    {
        Assert.Empty(Run(
            ("OnSelect", "ClearCollect(colItens, [1]); Notify(CountRows(colItens))")).Diagnostics);
    }

    [Fact]
    public void TrulyUnusedCollectionIsStillReported()
    {
        Assert.Single(Run(("OnSelect", "ClearCollect(colOrfa, [1])")).Diagnostics);
    }
}
