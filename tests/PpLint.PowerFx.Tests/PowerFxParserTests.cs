namespace PpLint.PowerFx.Tests;

public class PowerFxParserTests
{
    [Fact]
    public void Parse_ValidExpressionSucceeds()
    {
        var result = PowerFxParser.Parse("Set(varTotal, 1 + 2)");
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Root);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_ChainedBehaviorFormulaSucceeds()
    {
        // Propriedades de comportamento (OnSelect) encadeiam com ';'.
        var result = PowerFxParser.Parse("Set(varA, 1); Set(varB, 2)");
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Root);
    }

    [Fact]
    public void Parse_UsesInvariantCultureSeparators()
    {
        // O .msapp guarda InvariantScript: vírgula separa argumentos, mesmo em máquina pt-BR.
        var result = PowerFxParser.Parse("If(2 > 1, \"sim\", \"nao\")");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Parse_SyntaxErrorFailsWithoutThrowing()
    {
        var result = PowerFxParser.Parse("Set(varTotal, ");
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_EmptyScriptSucceedsWithNoRoot()
    {
        var result = PowerFxParser.Parse("   ");
        Assert.True(result.IsSuccess);
        Assert.Null(result.Root);
    }

    [Fact]
    public void Calls_FindsFunctionByNameCaseInsensitively()
    {
        var root = PowerFxParser.Parse("Set(varA, 1); Set(varB, 2)").Root!;
        Assert.Equal(2, AstWalker.Calls(root, "SET").Count());
    }

    [Fact]
    public void Calls_FindsNestedCalls()
    {
        var root = PowerFxParser.Parse("If(IsBlank(varX), Notify(\"vazio\"), Set(varY, 1))").Root!;
        Assert.Single(AstWalker.Calls(root, "Notify"));
        Assert.Single(AstWalker.Calls(root, "IsBlank"));
        Assert.Single(AstWalker.Calls(root, "Set"));
    }

    [Fact]
    public void FunctionName_ReturnsHeadName()
    {
        var root = PowerFxParser.Parse("Notify(\"oi\")").Root!;
        var call = Assert.Single(AstWalker.Calls(root, "Notify"));
        Assert.Equal("Notify", AstWalker.FunctionName(call));
    }

    [Fact]
    public void Identifiers_FindsAllFirstNames()
    {
        var root = PowerFxParser.Parse("Set(varTotal, varPreco * varQuantidade)").Root!;
        var names = AstWalker.Identifiers(root).Select(i => i.Ident.Name.Value).ToList();
        Assert.Contains("varTotal", names);
        Assert.Contains("varPreco", names);
        Assert.Contains("varQuantidade", names);
    }

    [Fact]
    public void Descendants_IncludesRootAndAllNodes()
    {
        var root = PowerFxParser.Parse("1 + 2").Root!;
        var all = AstWalker.Descendants(root).ToList();
        Assert.Contains(root, all);
        Assert.True(all.Count >= 3, "raiz mais os dois literais");
    }

    [Fact]
    public void Descendants_HandlesDeeplyNestedExpression()
    {
        var root = PowerFxParser.Parse("If(a, If(b, If(c, 1, 2), 3), 4)").Root!;
        Assert.Equal(3, AstWalker.Calls(root, "If").Count());
    }
}
