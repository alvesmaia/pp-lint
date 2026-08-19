using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;

namespace PpLint.Cli.Tests;

public class SarifReporterTests
{
    private static SourceLocation Loc(string artifact, int line = 0) =>
        new(artifact, "Controls/1.json", "btnA", line, 0);

    private static LintResult Result(string artifact, Severity severity = Severity.Warning, int line = 0) =>
        new(
            [new Diagnostic("NM010", RuleCategory.Naming, severity, "nome padrão", Loc(artifact, line))],
            [new RuleTally("NM010", RuleCategory.Naming, severity, 10, 1)]);

    private static JsonElement Render(params (string Path, LintResult Result)[] results) =>
        JsonDocument.Parse(
            SarifReporter.Render(AnalysisRun.From(results, TimeSpan.Zero))).RootElement;

    private static JsonElement FirstRun(JsonElement root) => root.GetProperty("runs")[0];

    [Fact]
    public void DeclaresSarifVersionAndSchema()
    {
        var root = Render(("a.msapp", Result("a.msapp")));

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.Contains("sarif-2.1.0", root.GetProperty("$schema").GetString());
    }

    [Fact]
    public void DriverCarriesToolIdentity()
    {
        var driver = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("tool").GetProperty("driver");

        Assert.Equal("pp-lint", driver.GetProperty("name").GetString());
        Assert.Contains("github.com", driver.GetProperty("informationUri").GetString());
    }

    [Fact]
    public void ReportedRulesAppearInTheDriverWithDocumentation()
    {
        var rules = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("tool").GetProperty("driver").GetProperty("rules");

        var rule = rules[0];
        Assert.Equal("NM010", rule.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            rule.GetProperty("shortDescription").GetProperty("text").GetString()));
        Assert.Contains("NM010", rule.GetProperty("helpUri").GetString());
        Assert.Equal("warning", rule.GetProperty("defaultConfiguration").GetProperty("level").GetString());
    }

    [Fact]
    public void OnlyReportedRulesAreDeclared()
    {
        // Declarar as 28 regras num relatório que achou uma polui a aba Security
        // com regras que não têm achado.
        var rules = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("tool").GetProperty("driver").GetProperty("rules");

        Assert.Equal(1, rules.GetArrayLength());
    }

    [Fact]
    public void ResultCarriesLevelAndMessage()
    {
        var result = FirstRun(Render(("a.msapp", Result("a.msapp")))).GetProperty("results")[0];

        Assert.Equal("NM010", result.GetProperty("ruleId").GetString());
        Assert.Equal("warning", result.GetProperty("level").GetString());
        Assert.Equal("nome padrão", result.GetProperty("message").GetProperty("text").GetString());
    }

    [Fact]
    public void InfoBecomesNote()
    {
        var result = FirstRun(Render(("a.msapp", Result("a.msapp", Severity.Info))))
            .GetProperty("results")[0];

        Assert.Equal("note", result.GetProperty("level").GetString());
    }

    [Fact]
    public void LocationPointsAtTheArtifactFile()
    {
        var location = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("results")[0].GetProperty("locations")[0];

        Assert.Equal(
            "a.msapp",
            location.GetProperty("physicalLocation").GetProperty("artifactLocation")
                .GetProperty("uri").GetString());
    }

    [Fact]
    public void NoRegionWhenThereIsNoLine()
    {
        // SARIF exige startLine >= 1. Nossa linha é 0 porque o .msapp guarda
        // fórmula dentro de JSON gerado: inventar linha 1 apontaria a anotação
        // para o lugar errado.
        var physical = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation");

        Assert.False(physical.TryGetProperty("region", out _));
    }

    [Fact]
    public void RegionAppearsWhenThereIsALine()
    {
        var physical = FirstRun(Render(("a.msapp", Result("a.msapp", line: 42))))
            .GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation");

        Assert.Equal(42, physical.GetProperty("region").GetProperty("startLine").GetInt32());
    }

    [Fact]
    public void EntryAndSymbolTravelAsLogicalLocations()
    {
        // A entrada dentro do pacote e o nome do controle não são posição de
        // texto; logicalLocations existe exatamente para isso.
        var logical = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("logicalLocations");

        var nomes = logical.EnumerateArray()
            .Select(l => l.GetProperty("fullyQualifiedName").GetString())
            .ToList();

        Assert.Contains(nomes, n => n!.Contains("Controls/1.json"));
        Assert.Contains(nomes, n => n!.Contains("btnA"));
    }

    [Fact]
    public void PathSeparatorsAreNormalizedToForwardSlash()
    {
        // URI de SARIF usa barra normal, independente do sistema onde rodou.
        var uri = FirstRun(Render((@"pasta\sub\a.msapp", Result(@"pasta\sub\a.msapp"))))
            .GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation").GetProperty("artifactLocation")
            .GetProperty("uri").GetString()!;

        Assert.DoesNotContain('\\', uri);
    }

    [Fact]
    public void EmptyRunProducesAValidEmptySarif()
    {
        var root = JsonDocument.Parse(
            SarifReporter.Render(AnalysisRun.From([], TimeSpan.Zero))).RootElement;

        Assert.Equal(0, FirstRun(root).GetProperty("results").GetArrayLength());
    }
}
