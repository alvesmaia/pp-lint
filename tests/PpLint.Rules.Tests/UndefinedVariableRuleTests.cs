using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class UndefinedVariableRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static PowerPlatformProject ProjectWith(string script, params string[] dataSources)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnOk.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);
        foreach (var ds in dataSources)
            app.DataSources.Add(new DataSource(ds, "SharePoint", ["Title"]));

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);
        return project;
    }

    private static LintResult Run(string script, params string[] dataSources) =>
        new RuleEngine([new UndefinedVariableRule()]).Run(ProjectWith(script, dataSources), PpLintConfig.Default);

    [Fact]
    public void ReportsNameThatWasNeverDefined()
    {
        var d = Assert.Single(Run("Notify(varNuncaDefinida)").Diagnostics);

        Assert.Equal("PF104", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("varNuncaDefinida", d.Message);
    }

    [Fact]
    public void AcceptsDefinedVariable() =>
        Assert.Empty(Run("Set(varTotal, 1); Notify(varTotal)").Diagnostics);

    [Fact]
    public void AcceptsControlReference() =>
        Assert.Empty(Run("Notify(btnOk.Text)").Diagnostics);

    [Fact]
    public void AcceptsScreenReference() =>
        Assert.Empty(Run("Navigate(scrHome)").Diagnostics);

    [Fact]
    public void AcceptsDataSource() =>
        Assert.Empty(Run("ClearCollect(colX, Filter(Pedidos, true))", "Pedidos").Diagnostics);

    [Fact]
    public void AcceptsBuiltinFunction() =>
        Assert.Empty(Run("Notify(Text(Now(), \"dd/mm\"))").Diagnostics);

    [Fact]
    public void AcceptsEnum() =>
        Assert.Empty(Run("Notify(\"oi\", NotificationType.Success)").Diagnostics);

    [Fact]
    public void AcceptsRowScope() =>
        Assert.Empty(Run("ForAll([1,2] As n, Notify(n))").Diagnostics);

    [Fact]
    public void AcceptsImplicitValueColumn() =>
        Assert.Empty(Run("Notify(Concat([1,2], Value))").Diagnostics);

    [Fact]
    public void AcceptsThisItem() =>
        Assert.Empty(Run("Notify(ThisItem.Title)").Diagnostics);

    [Fact]
    public void ReportsEachUndefinedNameOnce()
    {
        var result = Run("Notify(varTypo); Notify(varTypo)");

        Assert.Single(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Violations);
    }

    [Fact]
    public void EvaluatesOneTargetPerCandidateRead()
    {
        var result = Run("Set(varOk, 1); Notify(varOk); Notify(varTypo)");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}

public class UndefinedVariableRowScopeTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnOk.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new UndefinedVariableRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void UnknownNameInsideFilterIsNotReported()
    {
        // Dentro de Filter o nome pode ser coluna do registro; sem o schema da
        // tabela não dá para afirmar que é erro.
        Assert.Empty(Run("Notify(Filter(Tabela, Coluna = 1).Nome)").Diagnostics);
    }

    [Fact]
    public void UnknownNameAsFirstArgumentOfLookUpIsNotReported()
    {
        // LookUp(FonteDeDados, ...) — o primeiro argumento é a fonte, não variável.
        Assert.Empty(Run("Notify(LookUp(FonteDesconhecida, true).Id)").Diagnostics);
    }

    [Fact]
    public void UnknownNameOutsideAnyScopeIsStillReported()
    {
        Assert.Single(Run("Notify(varTypo)").Diagnostics);
    }
}
