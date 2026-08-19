using PpLint.Core.Configuration;
using PpLint.Core.Rules;
using PpLint.Rules.Naming;

namespace PpLint.Extractors.Tests;

/// <summary>
/// Valida os extractors contra artefatos reais, não contra os JSONs sintéticos
/// dos testes unitários. Os testes sobre o .msapp rodam sempre — o fixture é
/// versionado. Os testes sobre solução exportada são pulados até que alguém
/// coloque uma em tests/fixtures/ (veja o README de lá).
/// </summary>
public class RealArtifactTests
{
    private static string FixtureDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures"));

    private static string RealMsapp => Path.Combine(FixtureDir, "chess-real.msapp");

    private static string? SolutionFixture()
    {
        var path = Path.Combine(FixtureDir, "solucao-exemplo.zip");
        return File.Exists(path) ? path : null;
    }

    // ---- .msapp real, gerado pelo Power Apps Studio ----

    [Fact]
    public void RealMsapp_ExtractsScreens()
    {
        var project = ProjectLoader.Load(RealMsapp);

        var app = Assert.Single(project.Apps);
        Assert.True(app.Screens.Count >= 5, $"esperava várias telas, veio {app.Screens.Count}");
    }

    [Fact]
    public void RealMsapp_ExtractsControlsWithFormulas()
    {
        var project = ProjectLoader.Load(RealMsapp);
        var controls = project.Apps[0].AllControls().ToList();

        Assert.True(controls.Count > 100, $"esperava centenas de controles, veio {controls.Count}");
        Assert.True(
            controls.Count(c => c.Properties.Count > 0) > 50,
            "quase nenhum controle trouxe fórmulas — verifique Controls/*.json e o campo InvariantScript");
    }

    [Fact]
    public void RealMsapp_ReadsTemplateNames()
    {
        var project = ProjectLoader.Load(RealMsapp);
        var templates = project.Apps[0].AllControls().Select(c => c.TemplateName).Distinct().ToList();

        Assert.Contains("button", templates);
        Assert.Contains("label", templates);
        Assert.DoesNotContain(templates, string.IsNullOrEmpty);
    }

    [Fact]
    public void RealMsapp_FormulasParseAsPowerFx()
    {
        // O parser precisa aceitar Power Fx de verdade: se a cultura ou o modo
        // de encadeamento estiverem errados, quase tudo falharia no parse.
        var project = ProjectLoader.Load(RealMsapp);
        var scripts = project.Apps[0].AllControls()
            .SelectMany(c => c.Properties)
            .Select(p => p.Script)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var parsed = scripts.Count(s => PpLint.PowerFx.PowerFxParser.Parse(s).IsSuccess);

        Assert.True(
            parsed >= scripts.Count * 0.95,
            $"apenas {parsed} de {scripts.Count} fórmulas reais foram parseadas");
    }

    [Fact]
    public void RealMsapp_RulesRunAndFindKnownIssues()
    {
        var project = ProjectLoader.Load(RealMsapp);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        // Toda regra do catálogo produz uma contagem — o número cresce a cada
        // fase, então a asserção compara com o catálogo e não com um literal.
        var idsDoCatalogo = typeof(DefaultControlNameRule).Assembly.GetTypes()
            .Select(t => t.GetCustomAttributes(typeof(RuleAttribute), false).FirstOrDefault())
            .OfType<RuleAttribute>()
            .Select(a => a.Id)
            .Order();

        Assert.Equal(idsDoCatalogo, result.Tallies.Select(t => t.RuleId).Order());
        Assert.All(result.Tallies, t => Assert.True(t.Violations <= t.Evaluated));

        // Este app tem controles com nome padrão (Image1, Slider1, Rectangle11)
        // e duas variáveis globais nunca lidas — achados verificados manualmente.
        Assert.Contains(result.Diagnostics, d => d.RuleId == "NM010");
        Assert.Contains(result.Diagnostics, d => d.RuleId == "PF101");
    }

    [Fact]
    public void RealMsapp_DoesNotFlagStudioGeneratedControls()
    {
        // galleryTemplate e dataCard nascem prontos do Studio; cobrá-los por
        // convenção de nome seria reclamar de código que ninguém escreveu.
        var project = ProjectLoader.Load(RealMsapp);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Message.Contains("galleryTemplate") || d.Message.Contains("DataCard"));
    }

    [Fact]
    public void RealMsapp_PascalTypePresetFitsTheAppFarBetter()
    {
        // O app real nomeia como ButtonCreateGame/LabelPlayers. Com o preset
        // default ele recebe mais de cem avisos de prefixo; com o preset que
        // corresponde à sua convenção, quase nenhum. É esse o ponto da Fase 2a.
        var project = ProjectLoader.Load(RealMsapp);
        var engine = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly);

        var camel = ConfigResolver.Resolve(
            new ConfigFile { Preset = "camel-prefix" }, CliOverrides.None);
        var pascal = ConfigResolver.Resolve(
            new ConfigFile { Preset = "pascal-type" }, CliOverrides.None);

        var comCamel = engine.Run(project, camel).Diagnostics.Count(d => d.RuleId == "NM011");
        var comPascal = engine.Run(project, pascal).Diagnostics.Count(d => d.RuleId == "NM011");

        Assert.True(comCamel > 50, $"esperava muitos avisos com camel-prefix, veio {comCamel}");
        Assert.True(
            comPascal < comCamel / 2,
            $"pascal-type devia reduzir bastante: camel={comCamel}, pascal={comPascal}");
    }

    [Fact]
    public void RealMsapp_UndefinedVariableRuleDoesNotFloodWithFalsePositives()
    {
        // PF104 tem severidade Error: um falso positivo quebra o build de quem
        // confiou na ferramenta. Este app tem 827 fórmulas reais e funciona —
        // um punhado de achados é plausível, dezenas significam buraco no
        // resolvedor de símbolos, não app ruim.
        var project = ProjectLoader.Load(RealMsapp);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        var achados = result.Diagnostics.Where(d => d.RuleId == "PF104").ToList();

        Assert.True(
            achados.Count <= 5,
            "PF104 achou nomes demais num app real que funciona; o resolvedor está deixando "
            + "passar alguma categoria de símbolo: "
            + string.Join(", ", achados.Take(15).Select(d => d.Message)));
    }

    // ---- solução exportada (aguardando fixture) ----

    [SkippableFact]
    public void RealSolution_ExtractsAppsOrFlows()
    {
        var path = SolutionFixture();
        Skip.If(path is null, "Fixture tests/fixtures/solucao-exemplo.zip não encontrado.");

        var project = ProjectLoader.Load(path!);

        Assert.NotNull(project.Solution);
        Assert.True(
            project.Apps.Count > 0 || project.Flows.Count > 0,
            "Nenhum app nem fluxo foi extraído da solução real — o formato diverge do esperado.");
    }

    [SkippableFact]
    public void RealSolution_FlowsHaveActions()
    {
        var path = SolutionFixture();
        Skip.If(path is null, "Fixture não encontrado.");

        var project = ProjectLoader.Load(path!);
        Skip.If(project.Flows.Count == 0, "A solução de exemplo não contém cloud flows.");

        Assert.True(
            project.Flows.Any(f => f.AllActions().Any()),
            "Nenhum fluxo trouxe ações — verifique properties.definition.actions.");
    }
}
