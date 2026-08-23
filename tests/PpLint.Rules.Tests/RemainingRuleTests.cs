using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;
using PpLint.Rules.Naming;

namespace PpLint.Rules.Tests;

public class RemainingFlowRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static LintResult Run(IRule rule, Action<CloudFlow> build)
    {
        var flow = new CloudFlow { Name = "MeuFluxo", Location = Loc("MeuFluxo") };
        build(flow);

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    private static FlowAction Acao(string nome, string tipo, string estado = "Succeeded", params string[] depois)
    {
        var a = new FlowAction { Name = nome, Type = tipo, Location = Loc(nome) };
        a.RunAfter.AddRange(depois);
        a.RunAfterStates.Add(estado);
        return a;
    }

    // ---- FL211 ----

    [Fact]
    public void FL211_ReportsErrorBranchThatTellsNobody()
    {
        var d = Assert.Single(Run(new SilentFailureHandlingRule(), f =>
        {
            f.Actions.Add(Acao("Gravar", "OpenApiConnection"));
            f.Actions.Add(Acao("MarcarErro", "SetVariable", "Failed", "Gravar"));
        }).Diagnostics);

        Assert.Equal("FL211", d.RuleId);
    }

    [Fact]
    public void FL211_AcceptsErrorBranchThatNotifies() =>
        Assert.Empty(Run(new SilentFailureHandlingRule(), f =>
        {
            f.Actions.Add(Acao("Gravar", "OpenApiConnection"));
            f.Actions.Add(Acao("Avisar", "OpenApiConnection", "Failed", "Gravar"));
        }).Diagnostics);

    [Fact]
    public void FL211_AcceptsErrorBranchThatTerminates() =>
        Assert.Empty(Run(new SilentFailureHandlingRule(), f =>
        {
            f.Actions.Add(Acao("Gravar", "OpenApiConnection"));
            f.Actions.Add(Acao("Parar", "Terminate", "Failed", "Gravar"));
        }).Diagnostics);

    [Fact]
    public void FL211_StaysSilentWhenThereIsNoErrorHandlingAtAll()
    {
        // Fluxo sem tratamento nenhum é assunto da FL210; falar aqui também
        // daria dois achados para o mesmo problema.
        Assert.Empty(Run(new SilentFailureHandlingRule(), f =>
        {
            f.Actions.Add(Acao("A", "OpenApiConnection"));
            f.Actions.Add(Acao("B", "OpenApiConnection", "Succeeded", "A"));
        }).Diagnostics);
    }

    // ---- FL223 ----

    [Fact]
    public void FL223_ReportsSequentialLoop()
    {
        var d = Assert.Single(Run(new SequentialLoopRule(), f =>
            f.Actions.Add(Acao("Laco", "Foreach"))).Diagnostics);

        Assert.Equal("FL223", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void FL223_AcceptsLoopWithConcurrency() =>
        Assert.Empty(Run(new SequentialLoopRule(), f =>
            f.Actions.Add(new FlowAction
            {
                Name = "Laco",
                Type = "Foreach",
                Location = Loc("Laco"),
                ConcurrencyDegree = 20,
            })).Diagnostics);

    // ---- FL231 ----

    [Fact]
    public void FL231_ReportsEventTriggerWithoutConditionThatBranchesFirst()
    {
        var d = Assert.Single(Run(new MissingTriggerConditionRule(), f =>
        {
            f.Trigger = new FlowTrigger("Quando", "OpenApiConnection", Loc("Quando"));
            f.Actions.Add(Acao("Verificar", "If"));
        }).Diagnostics);

        Assert.Equal("FL231", d.RuleId);
    }

    [Fact]
    public void FL231_AcceptsTriggerThatAlreadyHasACondition() =>
        Assert.Empty(Run(new MissingTriggerConditionRule(), f =>
        {
            f.Trigger = new FlowTrigger("Quando", "OpenApiConnection", Loc("Quando"));
            f.TriggerHasCondition = true;
            f.Actions.Add(Acao("Verificar", "If"));
        }).Diagnostics);

    [Fact]
    public void FL231_IgnoresRecurrenceTriggers()
    {
        // Recorrência não tem condição de gatilho para configurar.
        Assert.Empty(Run(new MissingTriggerConditionRule(), f =>
        {
            f.Trigger = new FlowTrigger("R", "Recurrence", Loc("R"), new FlowRecurrence("Day", 1));
            f.Actions.Add(Acao("Verificar", "If"));
        }).Diagnostics);
    }

    [Fact]
    public void FL231_AcceptsFlowThatActsImmediately() =>
        Assert.Empty(Run(new MissingTriggerConditionRule(), f =>
        {
            f.Trigger = new FlowTrigger("Quando", "OpenApiConnection", Loc("Quando"));
            f.Actions.Add(Acao("Gravar", "OpenApiConnection"));
        }).Diagnostics);
}

public class RemainingNamingRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult RunOnApp(IRule rule, Action<CanvasApp> build)
    {
        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        build(app);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    private static Control Ctl(string nome, string template) =>
        new() { Name = nome, TemplateName = template, Location = Loc(nome) };

    private static Control Tela(string nome) =>
        new() { Name = nome, TemplateName = "screen", IsScreen = true, Location = Loc(nome) };

    // ---- NM012 ----

    [Fact]
    public void NM012_ReportsScreenOutsideThePattern()
    {
        var d = Assert.Single(RunOnApp(new ScreenNamingRule(), a => a.Screens.Add(Tela("Home"))).Diagnostics);

        Assert.Equal("NM012", d.RuleId);
    }

    [Fact]
    public void NM012_AcceptsScreenInThePattern() =>
        Assert.Empty(RunOnApp(new ScreenNamingRule(), a => a.Screens.Add(Tela("scrHome"))).Diagnostics);

    // ---- NM013 ----

    [Fact]
    public void NM013_ReportsComponentOutsideThePattern()
    {
        var d = Assert.Single(RunOnApp(new ComponentNamingRule(), a =>
        {
            var t = Tela("scrHome");
            t.AddChild(Ctl("Cabecalho", "component"));
            a.Screens.Add(t);
        }).Diagnostics);

        Assert.Equal("NM013", d.RuleId);
    }

    [Fact]
    public void NM013_IgnoresOrdinaryControls() =>
        Assert.Empty(RunOnApp(new ComponentNamingRule(), a =>
        {
            var t = Tela("scrHome");
            t.AddChild(Ctl("btnOk", "button"));
            a.Screens.Add(t);
        }).Diagnostics);

    // ---- NM041 ----

    [Fact]
    public void NM041_ReportsNameTooShortAfterThePrefix()
    {
        var achados = RunOnApp(new ShortNameRule(), a =>
        {
            var t = Tela("scrPedidos");
            t.AddChild(Ctl("btnA", "button"));
            a.Screens.Add(t);
        }).Diagnostics;

        var d = Assert.Single(achados, x => x.Message.Contains("btnA"));

        Assert.Equal("NM041", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void NM041_AcceptsDescriptiveName() =>
        Assert.Empty(RunOnApp(new ShortNameRule(), a =>
        {
            var t = Tela("scrPedidos");
            t.AddChild(Ctl("btnConfirmar", "button"));
            a.Screens.Add(t);
        }).Diagnostics);
}

public class FlowNamingRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static LintResult Run(IRule rule, string nomeFluxo, params string[] acoes)
    {
        var flow = new CloudFlow { Name = nomeFluxo, Location = Loc(nomeFluxo) };
        foreach (var a in acoes)
            flow.Actions.Add(new FlowAction { Name = a, Type = "Compose", Location = Loc(a) });

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void NM030_ReportsPortalDefaultName()
    {
        var d = Assert.Single(Run(new FlowDefaultNameRule(), "Fluxo sem título").Diagnostics);

        Assert.Equal("NM030", d.RuleId);
    }

    [Fact]
    public void NM030_ReportsCopySuffix() =>
        Assert.Single(Run(new FlowDefaultNameRule(), "Aprovar pedido (2)").Diagnostics);

    [Fact]
    public void NM030_AcceptsDescriptiveName() =>
        Assert.Empty(Run(new FlowDefaultNameRule(), "Aprovar pedido de compra").Diagnostics);

    [Fact]
    public void NM031_ReportsGeneratedActionName()
    {
        var d = Assert.Single(Run(new ActionDefaultNameRule(), "MeuFluxo", "Compose_3").Diagnostics);

        Assert.Equal("NM031", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void NM031_AcceptsTheFirstUnnumberedOperation()
    {
        // 'Compose' sozinho é o nome da primeira; num fluxo com uma só, é
        // aceitável. O sufixo numérico é que denuncia o descuido.
        Assert.Empty(Run(new ActionDefaultNameRule(), "MeuFluxo", "Compose").Diagnostics);
    }

    [Fact]
    public void NM031_AcceptsNamedAction() =>
        Assert.Empty(Run(new ActionDefaultNameRule(), "MeuFluxo", "Montar_corpo_do_email").Diagnostics);
}
