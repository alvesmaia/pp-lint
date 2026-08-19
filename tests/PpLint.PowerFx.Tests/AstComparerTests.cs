using Microsoft.PowerFx.Syntax;
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
    public void CaseIsPreservedEvenForIdentifiers()
    {
        // Poderíamos achatar a caixa dos nomes, já que o Power Fx não a
        // diferencia — mas o mesmo achatamento estragaria literais de texto
        // ("Sim" vs "sim"). Preferimos o falso negativo raro ao falso positivo.
        var (a, b) = Parse("varTotal", "VARTOTAL");
        Assert.False(AstComparer.AreEquivalent(a, b));
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

public class AstComparerLiteralAndCultureTests
{
    private static (Microsoft.PowerFx.Syntax.TexlNode A, Microsoft.PowerFx.Syntax.TexlNode B) Parse(
        string a, string b) =>
        (PowerFxParser.Parse(a).Root!, PowerFxParser.Parse(b).Root!);

    [Fact]
    public void StringLiteralsDifferByCase()
    {
        // Power Fx compara texto respeitando caixa: "Sim" e "sim" são valores
        // diferentes, e tratá-los como iguais faz a PF113 acusar código correto.
        var (a, b) = Parse("\"Sim\"", "\"sim\"");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void FormatStringsDifferByCase()
    {
        // "mm" é minuto e "MM" é mês — confundi-los seria grave.
        var (a, b) = Parse("Text(varD, \"mm\")", "Text(varD, \"MM\")");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void RenderUsesInvariantSyntax()
    {
        // O .msapp guarda InvariantScript: vírgula separa argumentos e ponto é
        // decimal. Numa máquina pt-BR o ToString() padrão devolveria ';' e ',',
        // e a sugestão exibida não poderia ser colada de volta na fórmula.
        var antes = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("pt-BR");

            var texto = AstComparer.Render(PowerFxParser.Parse("If(varC, 1.5, 2)").Root!);

            Assert.Contains(",", texto);
            Assert.DoesNotContain(";", texto);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = antes;
        }
    }
}

public class AstQuoteTests
{
    private static TexlNode Parse(string s) => PowerFxParser.Parse(s).Root!;

    [Fact]
    public void QuoteCollapsesLineBreaks()
    {
        var quoted = AstComparer.Quote(Parse("If(\n    varA,\n    1,\n    2\n)"));

        Assert.DoesNotContain('\n', quoted);
        Assert.DoesNotContain("  ", quoted);
    }

    [Fact]
    public void QuoteTruncatesLongFormulas()
    {
        var longa = "\"" + new string('x', 300) + "\"";

        Assert.True(AstComparer.Quote(Parse(longa)).Length <= 70);
    }

    [Fact]
    public void QuoteLeavesShortFormulasIntact() =>
        Assert.Equal("varA + 1", AstComparer.Quote(Parse("varA + 1")));
}
