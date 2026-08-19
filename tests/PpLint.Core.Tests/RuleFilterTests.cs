using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Core.Tests;

public class RuleFilterTests
{
    private static PpLintConfig With(string[]? select = null, string[]? ignore = null) =>
        PpLintConfig.Default with
        {
            Select = new HashSet<string>(select ?? [], StringComparer.OrdinalIgnoreCase),
            Ignore = new HashSet<string>(ignore ?? [], StringComparer.OrdinalIgnoreCase),
        };

    [Fact]
    public void EmptySelectRunsEverything()
    {
        Assert.True(RuleFilter.ShouldRun("PF101", With()));
        Assert.True(RuleFilter.ShouldRun("NM010", With()));
    }

    [Fact]
    public void SelectByExactId()
    {
        var config = With(select: ["PF101"]);

        Assert.True(RuleFilter.ShouldRun("PF101", config));
        Assert.False(RuleFilter.ShouldRun("PF110", config));
    }

    [Fact]
    public void SelectByCategoryPrefix()
    {
        var config = With(select: ["NM"]);

        Assert.True(RuleFilter.ShouldRun("NM010", config));
        Assert.True(RuleFilter.ShouldRun("NM011", config));
        Assert.False(RuleFilter.ShouldRun("PF101", config));
    }

    [Fact]
    public void IgnoreBeatsSelect()
    {
        var config = With(select: ["NM"], ignore: ["NM011"]);

        Assert.True(RuleFilter.ShouldRun("NM010", config));
        Assert.False(RuleFilter.ShouldRun("NM011", config));
    }

    [Fact]
    public void IgnoreByCategoryPrefix()
    {
        Assert.False(RuleFilter.ShouldRun("FL201", With(ignore: ["FL"])));
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        Assert.True(RuleFilter.ShouldRun("PF101", With(select: ["pf101"])));
    }

    [Fact]
    public void PrefixDoesNotMatchAcrossCategories()
    {
        // "P" não deve selecionar PF101 por acidente: o prefixo é a parte alfabética inteira.
        Assert.False(RuleFilter.ShouldRun("PF101", With(select: ["P"])));
    }

    [Fact]
    public void PerArtifactIgnoreMatchesGlob()
    {
        var config = PpLintConfig.Default with
        {
            PerArtifactIgnores = new Dictionary<string, IReadOnlyList<string>>
            {
                ["**/Legado*.msapp"] = ["NM010"],
            },
        };

        Assert.True(RuleFilter.IsIgnoredForArtifact("NM010", "apps/LegadoVendas.msapp", config));
        Assert.False(RuleFilter.IsIgnoredForArtifact("NM011", "apps/LegadoVendas.msapp", config));
        Assert.False(RuleFilter.IsIgnoredForArtifact("NM010", "apps/NovoVendas.msapp", config));
    }

    [Fact]
    public void PerArtifactIgnoreAcceptsCategoryPrefix()
    {
        var config = PpLintConfig.Default with
        {
            PerArtifactIgnores = new Dictionary<string, IReadOnlyList<string>>
            {
                ["*.msapp"] = ["NM"],
            },
        };

        Assert.True(RuleFilter.IsIgnoredForArtifact("NM010", "App.msapp", config));
        Assert.False(RuleFilter.IsIgnoredForArtifact("PF101", "App.msapp", config));
    }

    [Fact]
    public void PerArtifactIgnoreNormalizesBackslashes()
    {
        var config = PpLintConfig.Default with
        {
            PerArtifactIgnores = new Dictionary<string, IReadOnlyList<string>>
            {
                ["**/apps/*.msapp"] = ["NM010"],
            },
        };

        Assert.True(RuleFilter.IsIgnoredForArtifact("NM010", @"C:\repo\apps\Vendas.msapp", config));
    }

    [Fact]
    public void WithoutPerArtifactIgnoresNothingIsIgnored()
    {
        Assert.False(RuleFilter.IsIgnoredForArtifact("NM010", "App.msapp", PpLintConfig.Default));
    }
}
