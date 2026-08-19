using PpLint.Core;
using PpLint.Core.Configuration;

namespace PpLint.Core.Tests;

public class NamingPresetsTests
{
    [Fact]
    public void DefaultPresetIsCamelPrefix()
    {
        Assert.Equal("camel-prefix", NamingPresets.DefaultName);
    }

    [Fact]
    public void CamelPrefixUsesShortPrefixes()
    {
        Assert.True(NamingPresets.TryGet("camel-prefix", out var config));
        Assert.Equal("btn", config.ControlPrefixes["button"]);
        Assert.Equal("lbl", config.ControlPrefixes["label"]);
        Assert.Equal("^var[A-Z][A-Za-z0-9]*$", config.GlobalVariable);
    }

    [Fact]
    public void PascalTypeUsesFullTypeNames()
    {
        Assert.True(NamingPresets.TryGet("pascal-type", out var config));
        Assert.Equal("Button", config.ControlPrefixes["button"]);
        Assert.Equal("Label", config.ControlPrefixes["label"]);
        Assert.Equal("Gallery", config.ControlPrefixes["gallery"]);
    }

    [Fact]
    public void PascalTypeKeepsVariableConventionInPascalCase()
    {
        Assert.True(NamingPresets.TryGet("pascal-type", out var config));
        Assert.Matches(config.GlobalVariable, "VarTotal");
        Assert.DoesNotMatch(config.GlobalVariable, "varTotal");
    }

    [Fact]
    public void BothPresetsCoverTheSameTemplates()
    {
        NamingPresets.TryGet("camel-prefix", out var camel);
        NamingPresets.TryGet("pascal-type", out var pascal);

        Assert.Equal(
            camel.ControlPrefixes.Keys.OrderBy(k => k, StringComparer.Ordinal),
            pascal.ControlPrefixes.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void UnknownPresetIsRejected()
    {
        Assert.False(NamingPresets.TryGet("inventado", out _));
    }

    [Fact]
    public void PresetLookupIsCaseInsensitive()
    {
        Assert.True(NamingPresets.TryGet("Camel-Prefix", out _));
    }

    [Fact]
    public void NamesListsEveryPreset()
    {
        Assert.Contains("camel-prefix", NamingPresets.Names);
        Assert.Contains("pascal-type", NamingPresets.Names);
    }

    [Fact]
    public void GeneratedTemplatesAreExcludedInEveryPreset()
    {
        foreach (var name in NamingPresets.Names)
        {
            NamingPresets.TryGet(name, out var config);
            Assert.Contains("galleryTemplate", config.GeneratedControlTemplates);
        }
    }
}
