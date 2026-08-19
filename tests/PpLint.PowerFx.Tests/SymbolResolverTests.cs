using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx.Tests;

public class SymbolResolverTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static CanvasApp AppWith()
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);
        app.DataSources.Add(new DataSource("Pedidos", "SharePoint", ["Title"]));
        return app;
    }

    private static readonly IReadOnlySet<string> NoScopes = new HashSet<string>();

    [Fact]
    public void ControlIsResolved() =>
        Assert.Equal(SymbolKind.Control, SymbolResolver.Build(AppWith()).Resolve("btnOk", NoScopes));

    [Fact]
    public void ScreenIsResolved() =>
        Assert.Equal(SymbolKind.Screen, SymbolResolver.Build(AppWith()).Resolve("scrHome", NoScopes));

    [Fact]
    public void DataSourceIsResolved() =>
        Assert.Equal(SymbolKind.DataSource, SymbolResolver.Build(AppWith()).Resolve("Pedidos", NoScopes));

    [Fact]
    public void FunctionIsResolved() =>
        Assert.Equal(SymbolKind.Function, SymbolResolver.Build(AppWith()).Resolve("Filter", NoScopes));

    [Fact]
    public void EnumIsResolved() =>
        Assert.Equal(SymbolKind.Enum, SymbolResolver.Build(AppWith()).Resolve("Color", NoScopes));

    [Fact]
    public void RowScopeIsResolved()
    {
        var scopes = new HashSet<string>(["pedido"], StringComparer.OrdinalIgnoreCase);

        Assert.Equal(SymbolKind.RowScope, SymbolResolver.Build(AppWith()).Resolve("pedido", scopes));
    }

    [Fact]
    public void UnknownNameStaysUnknown() =>
        Assert.Equal(SymbolKind.Unknown, SymbolResolver.Build(AppWith()).Resolve("varTotal", NoScopes));

    [Fact]
    public void OnlyUnknownIsVariableCandidate()
    {
        var resolver = SymbolResolver.Build(AppWith());

        Assert.True(resolver.IsVariableCandidate("varTotal", NoScopes));
        Assert.False(resolver.IsVariableCandidate("btnOk", NoScopes));
        Assert.False(resolver.IsVariableCandidate("Filter", NoScopes));
        Assert.False(resolver.IsVariableCandidate("Color", NoScopes));
    }

    [Fact]
    public void ResolutionIsCaseInsensitive()
    {
        var resolver = SymbolResolver.Build(AppWith());

        Assert.Equal(SymbolKind.Control, resolver.Resolve("BTNOK", NoScopes));
        Assert.Equal(SymbolKind.DataSource, resolver.Resolve("pedidos", NoScopes));
    }

    [Fact]
    public void RowScopeWinsOverEverythingElse()
    {
        // Um `As` pode sombrear um nome de controle dentro daquela expressão.
        var scopes = new HashSet<string>(["btnOk"], StringComparer.OrdinalIgnoreCase);

        Assert.Equal(SymbolKind.RowScope, SymbolResolver.Build(AppWith()).Resolve("btnOk", scopes));
    }
}
