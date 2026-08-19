namespace PpLint.PowerFx.Tests;

public class AstComparerTests
{
    private static (Microsoft.PowerFx.Syntax.TexlNode A, Microsoft.PowerFx.Syntax.TexlNode B) Parse(
        string a, string b) =>
        (PowerFxParser.Parse(a).Root!, PowerFxParser.Parse(b).Root!);

    [Fact]
    public void IdenticalExpressionsAreEquivalent()
    {
        var (a, b) = Parse("varTotal + 1", "varTotal + 1");
        Assert.True(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void WhitespaceDoesNotMatter()
    {
        var (a, b) = Parse("varTotal+1", "varTotal  +  1");
        Assert.True(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void CaseOfIdentifiersDoesNotMatter()
    {
        // Power Fx não diferencia maiúsculas em nomes.
        var (a, b) = Parse("varTotal", "VARTOTAL");
        Assert.True(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void DifferentOperandsAreNotEquivalent()
    {
        var (a, b) = Parse("varTotal + 1", "varTotal + 2");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void DifferentOperatorsAreNotEquivalent()
    {
        var (a, b) = Parse("varA + varB", "varA - varB");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void OperandOrderMatters()
    {
        // a - b não é b - a; o comparador é estrutural, não algébrico.
        var (a, b) = Parse("varA - varB", "varB - varA");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void CallsWithSameArgumentsAreEquivalent()
    {
        var (a, b) = Parse("IsBlank(varX)", "IsBlank(varX)");
        Assert.True(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void CallsWithDifferentArgumentsAreNotEquivalent()
    {
        var (a, b) = Parse("IsBlank(varX)", "IsBlank(varY)");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void NormalizeIsStableForTheSameExpression()
    {
        var (a, b) = Parse("If(varX, 1, 2)", "If( varX , 1 , 2 )");
        Assert.Equal(AstComparer.Normalize(a), AstComparer.Normalize(b));
    }

    [Fact]
    public void StringLiteralsDifferByContent()
    {
        var (a, b) = Parse("\"abc\"", "\"abd\"");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }
}
