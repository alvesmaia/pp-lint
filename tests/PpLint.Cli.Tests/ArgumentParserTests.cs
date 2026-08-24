using PpLint.Core;

namespace PpLint.Cli.Tests;

public class ArgumentParserTests
{
    [Fact]
    public void Parse_CheckWithSinglePath()
    {
        var r = ArgumentParser.Parse(["check", "MinhaSolucao.zip"]);
        Assert.True(r.IsSuccess);
        Assert.Equal(CliCommand.Check, r.Value!.Command);
        Assert.Equal(["MinhaSolucao.zip"], r.Value.Paths);
        Assert.Equal("text", r.Value.Format);
        // Sem --fail-on, o parser não opina: o default vem do ConfigResolver.
        Assert.Null(r.Value.FailOn);
    }

    [Fact]
    public void Parse_CheckWithMultiplePathsAndOptions()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "b.msapp", "--format", "json", "--fail-on", "warning", "--no-color"]);
        Assert.True(r.IsSuccess);
        Assert.Equal(["a.zip", "b.msapp"], r.Value!.Paths);
        Assert.Equal("json", r.Value.Format);
        Assert.Equal(Severity.Warning, r.Value.FailOn);
        Assert.True(r.Value.NoColor);
    }

    [Fact]
    public void Parse_ExplainCapturesRuleId()
    {
        var r = ArgumentParser.Parse(["explain", "PF101"]);
        Assert.True(r.IsSuccess);
        Assert.Equal(CliCommand.Explain, r.Value!.Command);
        Assert.Equal("PF101", r.Value.ExplainRuleId);
    }

    [Fact]
    public void Parse_NoArgsIsHelp()
    {
        var r = ArgumentParser.Parse([]);
        Assert.True(r.IsSuccess);
        Assert.Equal(CliCommand.Help, r.Value!.Command);
    }

    [Fact]
    public void Parse_UnknownCommandFails()
    {
        var r = ArgumentParser.Parse(["frobnicate"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("frobnicate", r.Error);
    }

    [Fact]
    public void Parse_CheckWithoutPathFails()
    {
        var r = ArgumentParser.Parse(["check"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("caminho", r.Error);
    }

    [Fact]
    public void Parse_OptionMissingValueFails()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--format"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("--format", r.Error);
    }

    [Fact]
    public void Parse_InvalidFormatFails()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--format", "pdf"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("pdf", r.Error);
    }

    [Fact]
    public void Parse_InvalidFailOnFails()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--fail-on", "critical"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("critical", r.Error);
    }
}

public class ArgumentParserConfigTests
{
    [Fact]
    public void ParsesSelectAsCommaSeparatedList()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--select", "NM,PF101"]);

        Assert.True(r.IsSuccess);
        Assert.Equal(["NM", "PF101"], r.Value!.Select);
    }

    [Fact]
    public void ParsesIgnore()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--ignore", "NM011"]);

        Assert.Equal(["NM011"], r.Value!.Ignore);
    }

    [Fact]
    public void ParsesConfigPath()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--config", "custom.toml"]);

        Assert.Equal("custom.toml", r.Value!.ConfigPath);
    }

    [Fact]
    public void FailOnIsNullWhenNotPassed()
    {
        var r = ArgumentParser.Parse(["check", "a.zip"]);

        Assert.Null(r.Value!.FailOn);
    }

    [Fact]
    public void FailOnIsCapturedWhenPassed()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--fail-on", "info"]);

        Assert.Equal(Severity.Info, r.Value!.FailOn);
    }

    [Fact]
    public void SelectWithoutValueFails()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--select"]);

        Assert.False(r.IsSuccess);
        Assert.Contains("--select", r.Error);
    }

    [Fact]
    public void SelectAndIgnoreDefaultToEmpty()
    {
        var r = ArgumentParser.Parse(["check", "a.zip"]);

        Assert.Empty(r.Value!.Select);
        Assert.Empty(r.Value.Ignore);
    }
}

