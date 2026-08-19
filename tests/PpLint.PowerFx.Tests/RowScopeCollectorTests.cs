namespace PpLint.PowerFx.Tests;

public class RowScopeCollectorTests
{
    private static IReadOnlySet<string> Collect(string script) =>
        RowScopeCollector.Collect(PowerFxParser.Parse(script).Root!);

    [Fact]
    public void ImplicitScopesAreAlwaysPresent()
    {
        var scopes = Collect("1 + 1");

        Assert.Contains("ThisItem", scopes);
        Assert.Contains("ThisRecord", scopes);
        Assert.Contains("Self", scopes);
        Assert.Contains("Parent", scopes);
    }

    [Fact]
    public void AsIntroducesAName()
    {
        Assert.Contains("pedido", Collect("ForAll(Pedidos As pedido, pedido.Total)"));
    }

    [Fact]
    public void WithFieldsBecomeScopeNames()
    {
        Assert.Contains("total", Collect("With({total: 10}, total * 2)"));
    }

    [Fact]
    public void WithSeveralFields()
    {
        var scopes = Collect("With({a: 1, b: 2}, a + b)");

        Assert.Contains("a", scopes);
        Assert.Contains("b", scopes);
    }

    [Fact]
    public void NestedAsIsCollected()
    {
        var scopes = Collect("ForAll(A As x, ForAll(B As y, x.Id & y.Id))");

        Assert.Contains("x", scopes);
        Assert.Contains("y", scopes);
    }

    [Fact]
    public void OrdinaryIdentifiersAreNotScopes()
    {
        Assert.DoesNotContain("varTotal", Collect("Set(varTotal, 1)"));
    }

    [Fact]
    public void UpdateContextRecordIsNotAScope()
    {
        // UpdateContext também recebe um record, mas seus campos são variáveis
        // de contexto. Tratá-los como escopo faria PF102 e NM002 cegarem.
        Assert.DoesNotContain("locFiltro", Collect("UpdateContext({locFiltro: 1})"));
    }

    [Fact]
    public void NavigateContextRecordIsNotAScope()
    {
        Assert.DoesNotContain("locId", Collect("Navigate(scrB, Fade, {locId: 7})"));
    }

    [Fact]
    public void ComparisonIsCaseInsensitive()
    {
        Assert.Contains("PEDIDO", Collect("ForAll(Pedidos As pedido, pedido.Total)"));
    }
}
