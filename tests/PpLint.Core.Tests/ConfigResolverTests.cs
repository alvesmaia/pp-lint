using PpLint.Core;
using PpLint.Core.Configuration;

namespace PpLint.Core.Tests;

public class ConfigResolverTests
{
    [Fact]
    public void WithoutAnythingUsesDefaultPreset()
    {
        var config = ConfigResolver.Resolve(ConfigFile.Empty, CliOverrides.None);

        Assert.Equal("camel-prefix", config.PresetName);
        Assert.Equal("btn", config.Naming.ControlPrefixes["button"]);
        Assert.Equal(Severity.Error, config.FailOn);
        Assert.Empty(config.Select);
        Assert.Empty(config.Ignore);
    }

    [Fact]
    public void PresetFromFileChangesPrefixes()
    {
        var config = ConfigResolver.Resolve(new ConfigFile { Preset = "pascal-type" }, CliOverrides.None);

        Assert.Equal("pascal-type", config.PresetName);
        Assert.Equal("Button", config.Naming.ControlPrefixes["button"]);
    }

    [Fact]
    public void UnknownPresetThrowsConfigExceptionListingValidOnes()
    {
        var ex = Assert.Throws<ConfigException>(() =>
            ConfigResolver.Resolve(new ConfigFile { Preset = "inventado" }, CliOverrides.None));

        Assert.Contains("inventado", ex.Message);
        Assert.Contains("camel-prefix", ex.Message);
    }

    [Fact]
    public void FilePrefixesOverrideOnlyWhatTheyMention()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile
            {
                ControlPrefixes = new Dictionary<string, string> { ["button"] = "bt" },
            },
            CliOverrides.None);

        Assert.Equal("bt", config.Naming.ControlPrefixes["button"]);
        Assert.Equal("lbl", config.Naming.ControlPrefixes["label"]); // veio do preset
    }

    [Fact]
    public void FileNamingPatternsOverridePreset()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile
            {
                NamingPatterns = new Dictionary<string, string>
                {
                    ["global-variable"] = "^g[A-Z].*$",
                    ["screen"] = "^s[A-Z].*$",
                },
            },
            CliOverrides.None);

        Assert.Equal("^g[A-Z].*$", config.Naming.GlobalVariable);
        Assert.Equal("^s[A-Z].*$", config.Naming.Screen);
        Assert.Equal("^loc[A-Z][A-Za-z0-9]*$", config.Naming.ContextVariable); // intocado
    }

    [Fact]
    public void UnknownNamingPatternKeyThrows()
    {
        var ex = Assert.Throws<ConfigException>(() => ConfigResolver.Resolve(
            new ConfigFile { NamingPatterns = new Dictionary<string, string> { ["cor-do-botao"] = "x" } },
            CliOverrides.None));

        Assert.Contains("cor-do-botao", ex.Message);
    }

    [Fact]
    public void CliIgnoreReplacesFileIgnore()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile { Ignore = ["PF101"] },
            new CliOverrides(Select: null, Ignore: ["NM011"], FailOn: null));

        Assert.Contains("NM011", config.Ignore);
        Assert.DoesNotContain("PF101", config.Ignore);
    }

    [Fact]
    public void CliFailOnWins()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile { FailOn = Severity.Error },
            new CliOverrides(null, null, Severity.Info));

        Assert.Equal(Severity.Info, config.FailOn);
    }

    [Fact]
    public void FileFailOnUsedWhenCliSaysNothing()
    {
        var config = ConfigResolver.Resolve(new ConfigFile { FailOn = Severity.Warning }, CliOverrides.None);

        Assert.Equal(Severity.Warning, config.FailOn);
    }

    [Fact]
    public void SeverityOverridesComeFromFile()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile
            {
                SeverityOverrides = new Dictionary<string, Severity> { ["NM011"] = Severity.Info },
            },
            CliOverrides.None);

        Assert.Equal(Severity.Info, config.SeverityOverrides["NM011"]);
    }

    [Fact]
    public void PerArtifactIgnoresAreCarried()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile
            {
                PerArtifactIgnores = new Dictionary<string, IReadOnlyList<string>>
                {
                    ["**/Legado*.msapp"] = ["NM010"],
                },
            },
            CliOverrides.None);

        Assert.Equal(["NM010"], config.PerArtifactIgnores["**/Legado*.msapp"]);
    }

    [Fact]
    public void SelectFromCliWins()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile { Select = ["NM"] },
            new CliOverrides(Select: ["PF"], Ignore: null, FailOn: null));

        Assert.Contains("PF", config.Select);
        Assert.DoesNotContain("NM", config.Select);
    }

    [Fact]
    public void ResolveIsPure()
    {
        var file = new ConfigFile { Preset = "pascal-type" };

        var first = ConfigResolver.Resolve(file, CliOverrides.None);
        var second = ConfigResolver.Resolve(file, CliOverrides.None);

        Assert.Equal(first.PresetName, second.PresetName);
        Assert.Equal(first.Naming.ControlPrefixes["button"], second.Naming.ControlPrefixes["button"]);
    }
}
