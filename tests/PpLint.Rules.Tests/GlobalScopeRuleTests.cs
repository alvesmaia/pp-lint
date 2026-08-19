using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class GlobalScopeRuleTests
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
    public void PF105_ReportsGlobalReadOnASingleScreen()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "Set(varFiltro, 1)"),
            ("scrA", "Text", "varFiltro"));

        var d = Assert.Single(Run(new GlobalUsedInSingleScreenRule(), project).Diagnostics);

        Assert.Equal("PF105", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
        Assert.Contains("varFiltro", d.Message);
        Assert.Contains("scrA", d.Message);
    }

    [Fact]
    public void PF105_AcceptsGlobalReadOnSeveralScreens()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "Set(varUsuario, 1)"),
            ("scrA", "Text", "varUsuario"),
            ("scrB", "Text", "varUsuario"));

        Assert.Empty(Run(new GlobalUsedInSingleScreenRule(), project).Diagnostics);
    }

    [Fact]
    public void PF105_IgnoresGlobalNeverRead()
    {
        // Global sem leitura nenhuma é assunto da PF101; contar aqui puniria
        // o mesmo problema duas vezes no índice.
        var project = ProjectWith(("scrA", "OnSelect", "Set(varOrfa, 1)"));
        var result = Run(new GlobalUsedInSingleScreenRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void PF105_IgnoresContextAndCollections()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "UpdateContext({locX: 1}); ClearCollect(colY, [1])"),
            ("scrA", "Text", "locX & Concat(colY, Value)"));

        Assert.Empty(Run(new GlobalUsedInSingleScreenRule(), project).Diagnostics);
    }

    [Fact]
    public void PF106_ReportsSameSetTwiceInTheSameFormula()
    {
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, 1); Set(varX, 1)"));
        var d = Assert.Single(Run(new RedundantSetRule(), project).Diagnostics);

        Assert.Equal("PF106", d.RuleId);
        Assert.Contains("varX", d.Message);
    }

    [Fact]
    public void PF106_AcceptsDifferentValues() =>
        Assert.Empty(Run(new RedundantSetRule(),
            ProjectWith(("scrA", "OnSelect", "Set(varX, 1); Set(varX, 2)"))).Diagnostics);

    [Fact]
    public void PF106_AcceptsDifferentVariables() =>
        Assert.Empty(Run(new RedundantSetRule(),
            ProjectWith(("scrA", "OnSelect", "Set(varX, 1); Set(varY, 1)"))).Diagnostics);

    [Fact]
    public void PF106_AcceptsSameSetInDifferentFormulas()
    {
        // Sem ordem de execução não dá para afirmar que uma torna a outra inútil.
        var project = ProjectWith(
            ("scrA", "OnSelect", "Set(varX, 1)"),
            ("scrA", "OnChange", "Set(varX, 1)"));

        Assert.Empty(Run(new RedundantSetRule(), project).Diagnostics);
    }

    [Fact]
    public void PF106_AcceptsNonLiteralValue()
    {
        // Set(varX, Now()) duas vezes produz valores diferentes.
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, Now()); Set(varX, Now())"));

        Assert.Empty(Run(new RedundantSetRule(), project).Diagnostics);
    }

    [Fact]
    public void PF106_EvaluatesEverySetWithALiteralValue()
    {
        // Os dois Set são alvos examinados; um deles é a violação.
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, 1); Set(varX, 1)"));
        var tally = Assert.Single(Run(new RedundantSetRule(), project).Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
