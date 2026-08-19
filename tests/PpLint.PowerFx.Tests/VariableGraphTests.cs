using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx.Tests;

public class VariableGraphTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static CanvasApp AppWith(params (string Property, string Script)[] properties)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        foreach (var (property, script) in properties)
            button.Properties.Add(new PowerFxProperty(property, script, Loc($"btnOk.{property}")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);
        return app;
    }

    [Fact]
    public void Build_FindsGlobalDefinedBySet()
    {
        var graph = VariableGraph.Build(AppWith(("OnSelect", "Set(varTotal, 10)")));
        Assert.Equal(["varTotal"], graph.Globals.Select(g => g.Name));
    }

    [Fact]
    public void Build_SetTargetAloneIsNotARead()
    {
        var graph = VariableGraph.Build(AppWith(("OnSelect", "Set(varTotal, 10)")));
        Assert.False(graph.IsRead("varTotal"));
    }

    [Fact]
    public void Build_IdentifierElsewhereCountsAsRead()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varTotal, 10)"),
            ("Text", "varTotal")));
        Assert.True(graph.IsRead("varTotal"));
    }

    [Fact]
    public void Build_ReadInsideTheValueOfAnotherSetCounts()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varA, 1); Set(varB, varA + 1)")));
        Assert.True(graph.IsRead("varA"));
        Assert.False(graph.IsRead("varB"));
    }

    [Fact]
    public void Build_SelfReferenceCountsAsRead()
    {
        var graph = VariableGraph.Build(AppWith(("OnSelect", "Set(varContador, varContador + 1)")));
        Assert.True(graph.IsRead("varContador"));
    }

    [Fact]
    public void Build_NameComparisonIsCaseInsensitive()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varTotal, 1)"),
            ("Text", "VARTOTAL")));
        Assert.True(graph.IsRead("varTotal"));
    }

    [Fact]
    public void Build_DuplicateDefinitionsAppearOnce()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varTotal, 1)"),
            ("OnChange", "Set(varTotal, 2)")));
        Assert.Single(graph.Globals);
    }

    [Fact]
    public void Build_IncludesAppLevelProperties()
    {
        var app = AppWith(("Text", "varUsuario"));
        app.AppProperties.Add(new PowerFxProperty("OnStart", "Set(varUsuario, User().Email)", Loc("App.OnStart")));

        var graph = VariableGraph.Build(app);
        Assert.Equal(["varUsuario"], graph.Globals.Select(g => g.Name));
        Assert.True(graph.IsRead("varUsuario"));
    }

    [Fact]
    public void Build_UnparseableExpressionIsIgnored()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varTotal, 1)"),
            ("Text", "Set(varOutra,")));
        Assert.Single(graph.Globals);
    }

    [Fact]
    public void Build_LocationPointsAtTheDefiningProperty()
    {
        var graph = VariableGraph.Build(AppWith(("OnSelect", "Set(varTotal, 10)")));
        Assert.Equal("btnOk.OnSelect", Assert.Single(graph.Globals).Location.Symbol);
    }
}
