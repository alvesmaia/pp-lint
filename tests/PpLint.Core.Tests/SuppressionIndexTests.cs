using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Suppression;

namespace PpLint.Core.Tests;

public class SuppressionIndexTests
{
    private static SourceLocation Loc(string? symbol, string entry = "Controls/1.json") =>
        new("app.msapp", entry, symbol, 0, 0);

    private static PowerPlatformProject ProjectWithFormula(string property, string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        button.Properties.Add(new PowerFxProperty(property, script, Loc($"btnOk.{property}")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "app.msapp" };
        project.Apps.Add(app);
        return project;
    }

    [Fact]
    public void FindsDirectiveInLineComment()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101\nSet(varX, 1)"));

        Assert.True(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void FindsDirectiveInBlockComment()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "/* pp-lint: disable=PF110 */ If(2 > 1, 1)"));

        Assert.True(index.IsSuppressed("PF110", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void DoesNotSuppressOtherRules()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101"));

        Assert.False(index.IsSuppressed("PF110", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void SuppressesSeveralRulesSeparatedByComma()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101,PF110"));

        Assert.True(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
        Assert.True(index.IsSuppressed("PF110", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void ToleratesSpacesAroundTheList()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable = PF101 , PF110"));

        Assert.True(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
        Assert.True(index.IsSuppressed("PF110", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void DirectiveOnPropertyAlsoSuppressesFindingsOnTheControl()
    {
        // NM011 reporta no controle; a diretiva vive numa propriedade dele.
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=NM011"));

        Assert.True(index.IsSuppressed("NM011", Loc("btnOk")));
    }

    [Fact]
    public void DoesNotLeakToAnotherControl()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101"));

        Assert.False(index.IsSuppressed("PF101", Loc("btnCancelar.OnSelect")));
    }

    [Fact]
    public void DoesNotLeakToAnotherEntry()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101"));

        Assert.False(index.IsSuppressed("PF101", Loc("btnOk.OnSelect", entry: "Controls/2.json")));
    }

    [Fact]
    public void RuleIdComparisonIsCaseInsensitive()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=pf101"));

        Assert.True(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void FindsDirectiveInFlowActionDescription()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F", "Workflows/f.json") };
        flow.Actions.Add(new FlowAction
        {
            Name = "Inicializar",
            Type = "InitializeVariable",
            Description = "temporário — pp-lint: disable=FL201",
            Location = Loc("Inicializar", "Workflows/f.json"),
        });

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        var index = SuppressionIndex.Build(project);

        Assert.True(index.IsSuppressed("FL201", Loc("Inicializar", "Workflows/f.json")));
    }

    [Fact]
    public void FormulaWithoutDirectiveProducesNoEntries()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "Set(varX, 1)"));

        Assert.Empty(index.Directives);
        Assert.False(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void EmptyIndexSuppressesNothing()
    {
        Assert.False(SuppressionIndex.Empty.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void MalformedDirectiveIsIgnored()
    {
        // Sem lista de regras não há o que silenciar; não pode virar "silencia tudo".
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable="));

        Assert.Empty(index.Directives);
    }
}
