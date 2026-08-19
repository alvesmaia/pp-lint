using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx.Tests;

public class VariableGraphTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    /// <summary>App com uma ou mais telas; as fórmulas entram na tela indicada.</summary>
    private static CanvasApp AppWith(params (string Screen, string Property, string Script)[] formulas)
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

        return app;
    }

    [Fact]
    public void SetProducesGlobal()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Set(varTotal, 1)")));

        var v = Assert.Single(graph.Definitions);
        Assert.Equal("varTotal", v.Name);
        Assert.Equal(VariableKind.Global, v.Kind);
        Assert.Null(v.Screen);
    }

    [Fact]
    public void UpdateContextProducesContextVariableOnItsOwnScreen()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "UpdateContext({locFiltro: 1})")));

        var v = Assert.Single(graph.Definitions);
        Assert.Equal("locFiltro", v.Name);
        Assert.Equal(VariableKind.Context, v.Kind);
        Assert.Equal("scrA", v.Screen);
    }

    [Fact]
    public void NavigateDefinesContextVariableOnTheDestinationScreen()
    {
        // A variável nasce na tela de destino, não na de origem.
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Navigate(scrB, Fade, {locId: 7})")));

        var v = Assert.Single(graph.Definitions);
        Assert.Equal("locId", v.Name);
        Assert.Equal(VariableKind.Context, v.Kind);
        Assert.Equal("scrB", v.Screen);
    }

    [Fact]
    public void ClearCollectProducesCollection()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "ClearCollect(colItens, Pedidos)")));

        var v = Assert.Single(graph.Definitions);
        Assert.Equal("colItens", v.Name);
        Assert.Equal(VariableKind.Collection, v.Kind);
    }

    [Fact]
    public void CollectAlsoProducesCollection()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Collect(colItens, {Id: 1})")));

        Assert.Equal(VariableKind.Collection, Assert.Single(graph.Definitions).Kind);
    }

    [Fact]
    public void ReadIsDetected()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "Text", "varTotal")));

        Assert.True(graph.IsRead("varTotal"));
    }

    [Fact]
    public void DefinitionAloneIsNotRead()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Set(varTotal, 1)")));

        Assert.False(graph.IsRead("varTotal"));
    }

    [Fact]
    public void ReadIsAttributedToItsScreen()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrB", "Text", "varTotal")));

        Assert.True(graph.IsReadInScreen("varTotal", "scrB"));
        Assert.False(graph.IsReadInScreen("varTotal", "scrA"));
    }

    [Fact]
    public void ScreensReadingListsEveryScreen()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "Text", "varTotal"),
            ("scrB", "Text", "varTotal")));

        Assert.Equal(["scrA", "scrB"], graph.ScreensReading("varTotal").Order());
    }

    [Fact]
    public void ControlNameIsNotAnUnresolvedRead()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "Text", "btnscrA.Text")));

        Assert.Empty(graph.UnresolvedReads);
    }

    [Fact]
    public void FunctionNameIsNotAnUnresolvedRead()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "Text", "Text(Now(), \"dd/mm\")")));

        Assert.Empty(graph.UnresolvedReads);
    }

    [Fact]
    public void RowScopeIsNotAnUnresolvedRead()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "Text", "ThisItem.Title")));

        Assert.Empty(graph.UnresolvedReads);
    }

    [Fact]
    public void ReadOfNeverDefinedNameIsUnresolved()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "Text", "varNuncaDefinida")));

        var unresolved = Assert.Single(graph.UnresolvedReads);
        Assert.Equal("varNuncaDefinida", unresolved.Name);
    }

    [Fact]
    public void DefinedVariableIsNotUnresolved()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "Text", "varTotal")));

        Assert.Empty(graph.UnresolvedReads);
    }

    [Fact]
    public void DuplicateDefinitionsAppearOnce()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "OnChange", "Set(varTotal, 2)")));

        Assert.Single(graph.Definitions);
    }

    [Fact]
    public void GlobalsPropertyStillWorksForPF101()
    {
        // A PF101 já em produção depende desta propriedade.
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Set(varTotal, 1)")));

        Assert.Equal(["varTotal"], graph.Globals.Select(g => g.Name));
    }

    [Fact]
    public void AppLevelFormulasAreIncluded()
    {
        var app = AppWith(("scrA", "Text", "varUsuario"));
        app.AppProperties.Add(new PowerFxProperty("OnStart", "Set(varUsuario, User().Email)", Loc("App.OnStart")));

        var graph = VariableGraph.Build(app);

        Assert.Single(graph.Definitions);
        Assert.True(graph.IsRead("varUsuario"));
    }

    [Fact]
    public void UnparseableFormulaIsIgnored()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "Text", "Set(varOutra,")));

        Assert.Single(graph.Definitions);
    }
}
