using PpLint.Core;
using PpLint.Core.Configuration;

namespace PpLint.Core.Tests;

public class TomlConfigReaderTests
{
    [Fact]
    public void ReadsPreset()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            preset = "pascal-type"
            """);

        Assert.Equal("pascal-type", config.Preset);
    }

    [Fact]
    public void ReadsSelectAndIgnoreLists()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            select = ["NM", "PF101"]
            ignore = ["PF125"]
            """);

        Assert.Equal(["NM", "PF101"], config.Select);
        Assert.Equal(["PF125"], config.Ignore);
    }

    [Fact]
    public void ReadsFailOn()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            fail-on = "warning"
            """);

        Assert.Equal(Severity.Warning, config.FailOn);
    }

    [Fact]
    public void ReadsSeverityOverrides()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint.severity-overrides]
            NM011 = "info"
            PF101 = "error"
            """);

        Assert.Equal(Severity.Info, config.SeverityOverrides!["NM011"]);
        Assert.Equal(Severity.Error, config.SeverityOverrides["PF101"]);
    }

    [Fact]
    public void ReadsControlPrefixes()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint.naming.control-prefixes]
            button = "bt"
            label = "lb"
            """);

        Assert.Equal("bt", config.ControlPrefixes!["button"]);
        Assert.Equal("lb", config.ControlPrefixes["label"]);
    }

    [Fact]
    public void ReadsNamingPatterns()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint.naming]
            global-variable = "^g[A-Z].*$"
            screen = "^s[A-Z].*$"
            """);

        Assert.Equal("^g[A-Z].*$", config.NamingPatterns!["global-variable"]);
        Assert.Equal("^s[A-Z].*$", config.NamingPatterns["screen"]);
    }

    [Fact]
    public void ReadsPerArtifactIgnores()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint.per-artifact-ignores]
            "**/Legado*.msapp" = ["NM010", "NM011"]
            """);

        Assert.Equal(["NM010", "NM011"], config.PerArtifactIgnores!["**/Legado*.msapp"]);
    }

    [Fact]
    public void AbsentFieldsStayNull()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            preset = "camel-prefix"
            """);

        Assert.Null(config.Ignore);
        Assert.Null(config.FailOn);
        Assert.Null(config.ControlPrefixes);
        Assert.Null(config.PerArtifactIgnores);
    }

    [Fact]
    public void EmptyFileIsValidAndEmpty()
    {
        var config = TomlConfigReader.Read("");

        Assert.Null(config.Preset);
        Assert.Null(config.Select);
    }

    [Fact]
    public void MalformedTomlThrowsConfigException()
    {
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("[pp-lint\npreset ="));
        Assert.Contains("TOML", ex.Message);
    }

    [Fact]
    public void UnknownSeverityThrowsConfigException()
    {
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("""
            [pp-lint]
            fail-on = "critico"
            """));

        Assert.Contains("critico", ex.Message);
    }

    [Fact]
    public void WrongTypeThrowsConfigExceptionNamingTheField()
    {
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("""
            [pp-lint]
            ignore = "PF125"
            """));

        Assert.Contains("ignore", ex.Message);
    }
}

public class TomlUnknownKeyTests
{
    [Fact]
    public void UnknownKeyInSectionThrows()
    {
        // 'fail_on' com underscore parseia limpo e o time acreditaria estar
        // configurando fail-on; o CI seguiria saindo 0 em avisos.
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("""
            [pp-lint]
            fail_on = "warning"
            """));

        Assert.Contains("fail_on", ex.Message);
        Assert.Contains("fail-on", ex.Message);
    }

    [Fact]
    public void KnownKeysAreAccepted()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            preset = "camel-prefix"
            select = ["NM"]
            ignore = ["PF101"]
            fail-on = "info"
            """);

        Assert.Equal("camel-prefix", config.Preset);
    }

    [Fact]
    public void FileWithoutSectionHeaderThrows()
    {
        // Sem [pp-lint] nada seria aplicado, mas o CLI diria que usou um arquivo.
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("""
            preset = "pascal-type"
            """));

        Assert.Contains("[pp-lint]", ex.Message);
    }

    [Fact]
    public void UnknownSubTableThrows()
    {
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("""
            [pp-lint.naming-rules]
            button = "btn"
            """));

        Assert.Contains("naming-rules", ex.Message);
    }
}

public class ThresholdConfigTests
{
    [Fact]
    public void ReadsRecurrenceThreshold()
    {
        var file = TomlConfigReader.Read("""
            [pp-lint.thresholds]
            min-recurrence-minutes = 5
            """);

        Assert.Equal(5, file.MinRecurrenceMinutes);
    }

    [Fact]
    public void ThresholdAbsentMeansDefault()
    {
        Assert.Null(TomlConfigReader.Read("[pp-lint]\npreset = \"camel-prefix\"").MinRecurrenceMinutes);
        Assert.Equal(15, ConfigResolver.Resolve(new ConfigFile(), CliOverrides.None).Thresholds.MinRecurrenceMinutes);
    }

    [Fact]
    public void ThresholdReachesTheResolvedConfig()
    {
        var file = TomlConfigReader.Read("[pp-lint.thresholds]\nmin-recurrence-minutes = 60");

        Assert.Equal(60, ConfigResolver.Resolve(file, CliOverrides.None).Thresholds.MinRecurrenceMinutes);
    }

    [Fact]
    public void NonNumericThresholdIsRejected()
    {
        // Um limiar escrito como texto viraria o default em silêncio, e o usuário
        // acharia que configurou algo.
        var erro = Assert.Throws<ConfigException>(() =>
            TomlConfigReader.Read("[pp-lint.thresholds]\nmin-recurrence-minutes = \"cinco\""));

        Assert.Contains("min-recurrence-minutes", erro.Message);
    }
}
