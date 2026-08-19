using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;

namespace PpLint.Cli.Tests;

public class JsonReporterTests
{
    private static SourceLocation Loc(string artifact) => new(artifact, "Controls/1.json", "btnA", 0, 0);

    private static LintResult Result(string artifact) =>
        new(
            [new Diagnostic("NM010", RuleCategory.Naming, Severity.Warning, "nome padrão", Loc(artifact))],
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, 10, 1)]);

    private static JsonElement Render(params (string Path, LintResult Result)[] results) =>
        JsonDocument.Parse(
            JsonReporter.Render(AnalysisRun.From(results, TimeSpan.FromMilliseconds(250)))).RootElement;

    [Fact]
    public void OutputIsValidJsonWithASchemaVersion()
    {
        var root = Render(("a.msapp", Result("a.msapp")));

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void CarriesToolNameAndVersion()
    {
        var tool = Render(("a.msapp", Result("a.msapp"))).GetProperty("tool");

        Assert.Equal("pp-lint", tool.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("version").GetString()));
    }

    [Fact]
    public void SummaryCountsBySeverity()
    {
        var summary = Render(("a.msapp", Result("a.msapp"))).GetProperty("summary");

        Assert.Equal(0, summary.GetProperty("errors").GetInt32());
        Assert.Equal(1, summary.GetProperty("warnings").GetInt32());
        Assert.Equal(0, summary.GetProperty("infos").GetInt32());
        Assert.Equal(0.25, summary.GetProperty("durationSeconds").GetDouble(), 2);
    }

    [Fact]
    public void EachArtifactCarriesItsOwnComplianceAndDiagnostics()
    {
        var artifacts = Render(
            ("a.msapp", Result("a.msapp")),
            ("b.msapp", Result("b.msapp"))).GetProperty("artifacts");

        Assert.Equal(2, artifacts.GetArrayLength());
        Assert.Equal("a.msapp", artifacts[0].GetProperty("path").GetString());
        Assert.Equal(90.0, artifacts[0].GetProperty("compliance").GetProperty("percent").GetDouble(), 1);
        Assert.Equal(1, artifacts[0].GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public void DiagnosticCarriesEveryFieldTheTerminalShows()
    {
        var d = Render(("a.msapp", Result("a.msapp")))
            .GetProperty("artifacts")[0].GetProperty("diagnostics")[0];

        Assert.Equal("NM010", d.GetProperty("ruleId").GetString());
        Assert.Equal("Naming", d.GetProperty("category").GetString());
        Assert.Equal("warning", d.GetProperty("severity").GetString());
        Assert.Equal("nome padrão", d.GetProperty("message").GetString());

        var loc = d.GetProperty("location");
        Assert.Equal("a.msapp", loc.GetProperty("artifact").GetString());
        Assert.Equal("Controls/1.json", loc.GetProperty("entry").GetString());
        Assert.Equal("btnA", loc.GetProperty("symbol").GetString());
    }

    [Fact]
    public void AccentsSurviveAsRealCharactersNotEscapes()
    {
        // O padrão do System.Text.Json escapa não-ASCII como \u00XX, o que torna
        // o arquivo ilegível para quem abre no editor. Em português isso atinge
        // quase toda mensagem.
        var json = JsonReporter.Render(AnalysisRun.From([("a.msapp", Result("a.msapp"))], TimeSpan.Zero));

        Assert.Contains("padrão", json);
        Assert.DoesNotContain("\\u00", json);
    }

    [Fact]
    public void EmptyRunStillProducesValidJson()
    {
        var root = JsonDocument.Parse(
            JsonReporter.Render(AnalysisRun.From([], TimeSpan.Zero))).RootElement;

        Assert.Equal(0, root.GetProperty("artifacts").GetArrayLength());
        Assert.Equal(100.0, root.GetProperty("compliance").GetProperty("percent").GetDouble(), 1);
    }
}
