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

    // ---- solução exportada real ----

    [SkippableFact]
    public void RealSolution_ExtractsManifestAndFlowInDepth()
    {
        var path = SolutionFixture();
        Skip.If(path is null, "Fixture não encontrado.");

        var project = ProjectLoader.Load(path!);

        // Solução real da PnP: manifesto, um cloud flow com gatilho de
        // recorrência, doze ações e duas variáveis inicializadas.
        Assert.Equal("AITimeManagementFlow", project.Solution!.UniqueName);
        Assert.Equal("ms", project.Solution.PublisherPrefix);
        Assert.False(project.Solution.Managed);

        var flow = Assert.Single(project.Flows);
        Assert.Equal("Recurrence", flow.Trigger!.Type);
        Assert.True(
            flow.AllActions().Count() >= 10,
            $"esperava várias ações, veio {flow.AllActions().Count()}");
        Assert.Equal(2, flow.Variables.Count);
    }

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
    [SkippableFact]
    public void RealSolution_FlowRulesMatchWhatTheFileShows()
    {
        var path = SolutionFixture();
        Skip.If(path is null, "Fixture não encontrado.");

        var project = ProjectLoader.Load(path!);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        var ids = result.Diagnostics.Select(d => d.RuleId).ToList();

        // Todo runAfter do arquivo exige "Succeeded", e não há campo description
        // em lugar nenhum — os dois achados foram conferidos linha a linha.
        Assert.Contains("FL210", ids);
        Assert.Contains("FL240", ids);

        // E os silêncios, que valem tanto quanto os achados: os dois Foreach são
        // irmãos de topo e não aninhados; a recorrência é diária; todo runAfter
        // aponta para ação existente; as saídas de Send_to_Email e Filter_array
        // são consumidas por outputs() e body(); e as duas variáveis são
        // inicializadas antes do laço que as usa.
        Assert.DoesNotContain("FL222", ids);
        Assert.DoesNotContain("FL230", ids);
        Assert.DoesNotContain("FL241", ids);
        Assert.DoesNotContain("FL203", ids);
        Assert.DoesNotContain("FL202", ids);
    }
    [Fact]
    public void RealMsapp_FlagsScreensWhoseNameForcesQuotedReferences()
    {
        // Onze telas do app real têm espaço no nome, e por causa disso toda
        // navegação escreve Navigate('Game Screen', …) — conferido no arquivo.
        // É o problema que a NM040 existe para pegar.
        var project = ProjectLoader.Load(RealMsapp);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        var achados = result.Diagnostics.Where(d => d.RuleId == "NM040").ToList();

        Assert.True(achados.Count >= 10, $"esperava as telas com espaço, veio {achados.Count}");
        Assert.All(achados, d => Assert.Contains("espaço", d.Message));
    }
    [Fact]
    public void RealMsapp_FindsDeadCodeAndDuplication()
    {
        // Cinco achados conferidos no arquivo: a tela Asset Screen que nenhum
        // Navigate alcança, dois controles invisíveis cujo nome aparece uma vez
        // só — a própria declaração — e duas fórmulas repetidas três vezes.
        var project = ProjectLoader.Load(RealMsapp);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        var ids = result.Diagnostics.Select(d => d.RuleId).ToList();

        Assert.Contains("DUP301", ids);
        Assert.Contains("DUP303", ids);
        Assert.Contains("DUP304", ids);
    }

    [Fact]
    public void RealMsapp_DoesNotFlagConnectorUsedOnlyThroughFunctions()
    {
        // Office365Users é usado em quatro telas, sempre como
        // Office365Users.UserPhotoV2(...). O nome vive no namespace da função e
        // não aparece como identificador solto: uma primeira versão da DUP305 o
        // acusou de não usado.
        var project = ProjectLoader.Load(RealMsapp);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.RuleId == "DUP305" && d.Message.Contains("Office365Users"));
    }
}

/// <summary>
/// O objeto App não é uma tela. Ele traz OnStart, OnError e StartScreen, e no
/// .msapp real vive num Controls/*.json com o template 'appinfo', ao lado das
/// telas de verdade.
/// </summary>
public class AppObjectTests
{
    private static string RealMsapp => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "chess-real.msapp"));

    [Fact]
    public void OnStartIsReachableAsAnAppProperty()
    {
        // O extractor procurava OnStart em Properties.json, que este .msapp não
        // tem: as fórmulas do App vêm como regras de um controle 'appinfo'.
        var app = ProjectLoader.Load(RealMsapp).Apps[0];

        var onStart = app.AppProperties.SingleOrDefault(p => p.Name == "OnStart");

        Assert.NotNull(onStart);
        Assert.True(onStart!.Script.Length > 1000, $"OnStart veio com {onStart.Script.Length} caracteres");
    }

    [Fact]
    public void AppIsNotCountedAsAScreen()
    {
        // Enquanto ele contava como tela, toda regra que percorre telas o
        // examinava — e as variáveis do OnStart ficavam escopadas numa tela
        // chamada 'App', que não existe.
        var app = ProjectLoader.Load(RealMsapp).Apps[0];

        Assert.DoesNotContain(app.Screens, s => s.Name == "App");
        Assert.All(app.Screens, s => Assert.Equal("screen", s.TemplateName));
    }

    [Fact]
    public void RealScreensAreStillThere()
    {
        var app = ProjectLoader.Load(RealMsapp).Apps[0];

        Assert.Equal(11, app.Screens.Count);
        Assert.Contains(app.Screens, s => s.Name == "Home Screen");
    }
}
