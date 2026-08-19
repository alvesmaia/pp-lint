using PpLint.Core;
using PpLint.Core.Configuration;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Naming;

namespace PpLint.Rules.Tests;

public class VariableNamingRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static PowerPlatformProject ProjectWithFormula(string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnOk.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);
        return project;
    }

    private static LintResult Run(IRule rule, string script, PpLintConfig? config = null) =>
        new RuleEngine([rule]).Run(ProjectWithFormula(script), config ?? PpLintConfig.Default);

    [Fact]
    public void NM001_ReportsGlobalOutsideTheConvention()
    {
        var d = Assert.Single(Run(new GlobalVariableNamingRule(), "Set(total, 1)").Diagnostics);

        Assert.Equal("NM001", d.RuleId);
        Assert.Contains("total", d.Message);
        Assert.Contains("var", d.Message);
    }

    [Fact]
    public void NM001_AcceptsConventionalName() =>
        Assert.Empty(Run(new GlobalVariableNamingRule(), "Set(varTotal, 1)").Diagnostics);

    [Fact]
    public void NM001_IgnoresContextAndCollections()
    {
        var result = Run(new GlobalVariableNamingRule(), "UpdateContext({errado: 1}); ClearCollect(tambemErrado, [1])");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void NM002_ReportsContextVariableOutsideTheConvention()
    {
        var d = Assert.Single(Run(new ContextVariableNamingRule(), "UpdateContext({filtro: 1})").Diagnostics);

        Assert.Equal("NM002", d.RuleId);
        Assert.Contains("loc", d.Message);
    }

    [Fact]
    public void NM002_AcceptsConventionalName() =>
        Assert.Empty(Run(new ContextVariableNamingRule(), "UpdateContext({locFiltro: 1})").Diagnostics);

    [Fact]
    public void NM003_ReportsCollectionOutsideTheConvention()
    {
        var d = Assert.Single(Run(new CollectionNamingRule(), "ClearCollect(itens, [1])").Diagnostics);

        Assert.Equal("NM003", d.RuleId);
        Assert.Contains("col", d.Message);
    }

    [Fact]
    public void NM003_AcceptsConventionalName() =>
        Assert.Empty(Run(new CollectionNamingRule(), "ClearCollect(colItens, [1])").Diagnostics);

    [Fact]
    public void PascalTypePresetChangesWhatIsAccepted()
    {
        var config = PpLintConfig.Default with
        {
            Naming = new NamingConfig { GlobalVariable = "^Var[A-Z][A-Za-z0-9]*$" },
        };

        Assert.Empty(Run(new GlobalVariableNamingRule(), "Set(VarTotal, 1)", config).Diagnostics);
        Assert.Single(Run(new GlobalVariableNamingRule(), "Set(varTotal, 1)", config).Diagnostics);
    }

    [Fact]
    public void EvaluatesOneTargetPerVariable()
    {
        var result = Run(new GlobalVariableNamingRule(), "Set(varA, 1); Set(errado, 2)");

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void InvalidRegexInConfigIsReportedNotCrashed()
    {
        // Regex quebrado é erro do usuário; a regra não pode derrubar a análise
        // nem fingir que está tudo certo.
        var config = PpLintConfig.Default with
        {
            Naming = new NamingConfig { GlobalVariable = "^var([A-Z" },
        };

        var ex = Assert.Throws<ConfigException>(() =>
            Run(new GlobalVariableNamingRule(), "Set(varTotal, 1)", config));

        Assert.Contains("global-variable", ex.Message);
    }
}
