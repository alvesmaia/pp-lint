using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Core.Tests;

public class ModelTests
{
    private static SourceLocation Loc(string? symbol) => new("a.msapp", "Controls/1.json", symbol, 0, 0);

    [Fact]
    public void AllControls_WalksTreeDepthFirstIncludingScreens()
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", Location = Loc("scrHome"), IsScreen = true };
        var container = new Control { Name = "cntTopo", TemplateName = "groupContainer", Location = Loc("cntTopo") };
        var button = new Control { Name = "btnSalvar", TemplateName = "button", Location = Loc("btnSalvar") };

        screen.AddChild(container);
        container.AddChild(button);

        var app = new CanvasApp { Name = "AppVendas", Location = Loc(null) };
        app.Screens.Add(screen);

        Assert.Equal(["scrHome", "cntTopo", "btnSalvar"], app.AllControls().Select(c => c.Name));
    }

    [Fact]
    public void AddChild_SetsParent()
    {
        var parent = new Control { Name = "scrHome", TemplateName = "screen", Location = Loc("scrHome"), IsScreen = true };
        var child = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        parent.AddChild(child);

        Assert.Same(parent, child.Parent);
        Assert.Contains(child, parent.Children);
    }

    [Fact]
    public void Control_ScreenOf_ReturnsOwningScreen()
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", Location = Loc("scrHome"), IsScreen = true };
        var container = new Control { Name = "cnt", TemplateName = "groupContainer", Location = Loc("cnt") };
        var button = new Control { Name = "btn", TemplateName = "button", Location = Loc("btn") };
        screen.AddChild(container);
        container.AddChild(button);

        Assert.Same(screen, button.ScreenOf());
        Assert.Same(screen, screen.ScreenOf());
    }

    [Fact]
    public void AllActions_WalksNestedActions()
    {
        var loop = new FlowAction { Name = "Apply_to_each", Type = "Foreach", Location = Loc("Apply_to_each") };
        var inner = new FlowAction { Name = "Get_item", Type = "OpenApiConnection", Location = Loc("Get_item") };
        loop.Children.Add(inner);

        var flow = new CloudFlow { Name = "AprovarPedido", Location = Loc(null) };
        flow.Actions.Add(loop);

        Assert.Equal(["Apply_to_each", "Get_item"], flow.AllActions().Select(a => a.Name));
    }

    [Fact]
    public void Project_StartsEmpty()
    {
        var p = new PowerPlatformProject { SourcePath = "x.zip" };
        Assert.Empty(p.Apps);
        Assert.Empty(p.Flows);
        Assert.Empty(p.Tables);
        Assert.Null(p.Solution);
    }
}
