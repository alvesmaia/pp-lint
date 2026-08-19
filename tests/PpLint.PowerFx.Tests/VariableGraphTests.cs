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
        // A variável nasce na tela de destino, não na de origem. A tela precisa
        // existir: navegação dinâmica cai no fallback e é testada à parte.
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Navigate(scrB, Fade, {locId: 7})"),
            ("scrB", "Text", "\"x\"")));

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

public class VariableGraphCacheTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static CanvasApp AppWith(string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);
        return app;
    }

    [Fact]
    public void SameAppReturnsTheSameGraph()
    {
        // Oito regras pedem o grafo do mesmo app na mesma execução; reconstruir
        // toda vez levava um app de 2.279 fórmulas de 1,6 s para 9 s.
        var app = AppWith("Set(varTotal, 1)");

        Assert.Same(VariableGraph.Build(app), VariableGraph.Build(app));
    }

    [Fact]
    public void DifferentAppsGetDifferentGraphs()
    {
        var primeiro = VariableGraph.Build(AppWith("Set(varA, 1)"));
        var segundo = VariableGraph.Build(AppWith("Set(varB, 1)"));

        Assert.NotSame(primeiro, segundo);
        Assert.Equal(["varA"], primeiro.Definitions.Select(d => d.Name));
        Assert.Equal(["varB"], segundo.Definitions.Select(d => d.Name));
    }
}

public class VariableGraphScopeAndSourceTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static CanvasApp AppWith(
        string[] dataSources,
        params (string Screen, string Property, string Script)[] formulas)
    {
        var app = new CanvasApp { Name = "App", Location = Loc(null) };

        foreach (var ds in dataSources)
            app.DataSources.Add(new DataSource(ds, "SharePoint", ["Title"]));

        foreach (var screenName in formulas.Select(f => f.Screen).Distinct())
        {
            var screen = new Control
            {
                Name = screenName, TemplateName = "screen", IsScreen = true, Location = Loc(screenName),
            };
            var button = new Control
            {
                Name = $"btn{screenName}", TemplateName = "button", Location = Loc($"btn{screenName}"),
            };

            foreach (var (_, property, script) in formulas.Where(f => f.Screen == screenName))
                button.Properties.Add(new PowerFxProperty(property, script, Loc($"btn{screenName}.{property}")));

            screen.AddChild(button);
            app.Screens.Add(screen);
        }

        return app;
    }

    [Fact]
    public void CollectIntoARealDataSourceIsNotACollection()
    {
        // Collect(Ativos, {...}) grava numa lista do SharePoint; Ativos não é
        // coleção e não pode ser cobrada por convenção 'col' nem por desuso.
        var graph = VariableGraph.Build(AppWith(
            ["Ativos"],
            ("scrA", "OnSelect", "Collect(Ativos, {Title: \"x\"})")));

        Assert.DoesNotContain(graph.Definitions, d => d.Kind == VariableKind.Collection);
    }

    [Fact]
    public void CollectIntoANewNameIsStillACollection()
    {
        var graph = VariableGraph.Build(AppWith(
            [],
            ("scrA", "OnSelect", "ClearCollect(colItens, [1])")));

        Assert.Single(graph.Definitions, d => d.Kind == VariableKind.Collection);
    }

    [Fact]
    public void ContextVariableWithTheSameNameOnTwoScreensIsTwoDefinitions()
    {
        // locX de scrA e locX de scrB são variáveis diferentes; guardar só uma
        // faria a PF102 perder o achado da outra.
        var graph = VariableGraph.Build(AppWith(
            [],
            ("scrA", "OnSelect", "UpdateContext({locX: 1})"),
            ("scrA", "Text", "locX"),
            ("scrB", "OnSelect", "UpdateContext({locX: 2})")));

        var contexto = graph.Definitions.Where(d => d.Kind == VariableKind.Context).ToList();

        Assert.Equal(2, contexto.Count);
        Assert.Equal(["scrA", "scrB"], contexto.Select(d => d.Screen).Order());
    }

    [Fact]
    public void NavigateToANameThatIsNotAScreenFallsBackToTheCurrentScreen()
    {
        // Navegação dinâmica: o destino é uma variável, não uma tela. Arquivar a
        // variável de contexto sob 'varProximaTela' faria a PF102 acusá-la sempre.
        var graph = VariableGraph.Build(AppWith(
            [],
            ("scrA", "OnSelect", "Navigate(varProximaTela, Fade, {locId: 7})")));

        var contexto = Assert.Single(graph.Definitions.Where(d => d.Kind == VariableKind.Context).ToList());
        Assert.Equal("scrA", contexto.Screen);
    }

    [Fact]
    public void NavigateToARealScreenStillFilesOnTheDestination()
    {
        var graph = VariableGraph.Build(AppWith(
            [],
            ("scrA", "OnSelect", "Navigate(scrB, Fade, {locId: 7})"),
            ("scrB", "Text", "\"x\"")));

        var contexto = Assert.Single(graph.Definitions.Where(d => d.Kind == VariableKind.Context).ToList());
        Assert.Equal("scrB", contexto.Screen);
    }
}
