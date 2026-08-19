using PpLint.Rules;

namespace PpLint.Rules.Tests;

public class RuleDocsTests
{
    [Fact]
    public void FindsAnExistingDocument()
    {
        var doc = RuleDocs.Find("NM010");

        Assert.NotNull(doc);
        Assert.Equal("NM010", doc!.Id);
    }

    [Fact]
    public void TitleComesFromTheFirstHeading()
    {
        var doc = RuleDocs.Find("NM010")!;

        Assert.False(string.IsNullOrWhiteSpace(doc.Title));
        Assert.DoesNotContain("#", doc.Title);
        Assert.DoesNotContain("NM010", doc.Title);
    }

    [Fact]
    public void SummaryComesFromWhatItCatches()
    {
        var doc = RuleDocs.Find("NM010")!;

        Assert.False(string.IsNullOrWhiteSpace(doc.Summary));
        Assert.DoesNotContain("##", doc.Summary);
        // O resumo é uma frase, não a seção inteira: vai para shortDescription
        // do SARIF, que os visualizadores mostram numa linha.
        Assert.True(doc.Summary.Length < 300, $"resumo longo demais: {doc.Summary.Length} caracteres");
    }

    [Fact]
    public void MarkdownKeepsTheWholeDocument()
    {
        var doc = RuleDocs.Find("NM010")!;

        Assert.Contains("## O que pega", doc.Markdown);
        Assert.Contains("## Por que importa", doc.Markdown);
        Assert.Contains("## Exemplo", doc.Markdown);
    }

    [Fact]
    public void UnknownRuleReturnsNull() => Assert.Null(RuleDocs.Find("XX999"));

    [Fact]
    public void LookupIsCaseInsensitive() => Assert.NotNull(RuleDocs.Find("nm010"));

    [Fact]
    public void AvailableIdsAreSorted()
    {
        var ids = RuleDocs.AvailableIds();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Fact]
    public void EveryAvailableDocumentParses()
    {
        // Um documento malformado não pode explodir só quando alguém pedir
        // 'explain' daquela regra específica.
        foreach (var id in RuleDocs.AvailableIds())
        {
            var doc = RuleDocs.Find(id);

            Assert.NotNull(doc);
            Assert.False(string.IsNullOrWhiteSpace(doc!.Title), $"{id} sem título");
            Assert.False(string.IsNullOrWhiteSpace(doc.Summary), $"{id} sem resumo");
        }
    }
}
