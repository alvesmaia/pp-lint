using PpLint.Core;

namespace PpLint.Core.Tests;

public class DiagnosticTests
{
    [Theory]
    [InlineData(Severity.Error, 10)]
    [InlineData(Severity.Warning, 3)]
    [InlineData(Severity.Info, 1)]
    public void SeverityWeights_MatchSpec(Severity severity, int expected)
        => Assert.Equal(expected, SeverityWeights.Of(severity));

    [Fact]
    public void SourceLocation_FormatsHumanReadablePath()
    {
        var loc = new SourceLocation("MinhaSolucao.zip", "CanvasApps/App.msapp", "btnSalvar.OnSelect", 12, 8);
        Assert.Equal("MinhaSolucao.zip > CanvasApps/App.msapp > btnSalvar.OnSelect:12:8", loc.ToString());
    }

    [Fact]
    public void SourceLocation_OmitsSymbolAndPositionWhenAbsent()
    {
        var loc = new SourceLocation("App.msapp", "Controls/1.json", null, 0, 0);
        Assert.Equal("App.msapp > Controls/1.json", loc.ToString());
    }

    [Fact]
    public void Diagnostic_CarriesRuleMetadata()
    {
        var loc = new SourceLocation("a.zip", "b.json", null, 0, 0);
        var d = new Diagnostic("NM010", RuleCategory.Naming, Severity.Error, "mensagem", loc);
        Assert.Equal("NM010", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }
}
