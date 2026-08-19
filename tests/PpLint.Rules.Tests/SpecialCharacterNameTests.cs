using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Naming;

namespace PpLint.Rules.Tests;

/// <summary>
/// NM040. Nome com acento, cedilha, til, vírgula, espaço ou qualquer coisa fora
/// de letra ASCII, dígito e underscore é erro — não é questão de estilo.
/// </summary>
public class SpecialCharacterNameTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult RunOnFormula(string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new SpecialCharacterNameRule()]).Run(project, PpLintConfig.Default);
    }

    private static LintResult RunOnControls(params (string Name, string Template)[] controls)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };

        foreach (var (name, template) in controls)
            screen.AddChild(new Control { Name = name, TemplateName = template, Location = Loc(name) });

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new SpecialCharacterNameRule()]).Run(project, PpLintConfig.Default);
    }

    private static LintResult RunOnFlow(Action<CloudFlow> build)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        build(flow);

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        return new RuleEngine([new SpecialCharacterNameRule()]).Run(project, PpLintConfig.Default);
    }

    // ---- variáveis ----

    [Fact]
    public void AccentInGlobalVariableIsAnError()
    {
        var d = Assert.Single(RunOnFormula("Set(varDescrição, \"x\")").Diagnostics);

        Assert.Equal("NM040", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("varDescrição", d.Message);
    }

    [Fact]
    public void CedillaIsAnError() =>
        Assert.Single(RunOnFormula("Set(varOperacao_Ação, 1)").Diagnostics);

    [Fact]
    public void TildeIsAnError() =>
        Assert.Single(RunOnFormula("Set(varNãoLido, 1)").Diagnostics);

    [Fact]
    public void ContextVariableWithAccentIsAnError() =>
        Assert.Single(RunOnFormula("UpdateContext({locSeleção: 1})").Diagnostics);

    [Fact]
    public void CollectionWithAccentIsAnError() =>
        Assert.Single(RunOnFormula("ClearCollect(colUsuários, [1])").Diagnostics);

    [Fact]
    public void PlainAsciiNamesAreAccepted() =>
        Assert.Empty(RunOnFormula(
            "Set(varTotal, 1); UpdateContext({locAberto: true}); ClearCollect(colItens, [1])").Diagnostics);

    [Fact]
    public void UnderscoreAndDigitsAreAccepted() =>
        Assert.Empty(RunOnFormula("Set(var_Total_2, 1)").Diagnostics);

    // ---- controles e telas ----

    [Fact]
    public void ControlWithAccentIsAnError()
    {
        var d = Assert.Single(RunOnControls(("lblDescrição", "label")).Diagnostics);

        Assert.Equal("NM040", d.RuleId);
        Assert.Contains("lblDescrição", d.Message);
    }

    [Fact]
    public void ControlWithSpaceIsAnError()
    {
        // Espaço obriga a citar o controle entre aspas simples em toda fórmula
        // que o referencie.
        Assert.Single(RunOnControls(("lbl Nome", "label")).Diagnostics);
    }

    [Fact]
    public void ControlWithHyphenIsAnError() =>
        Assert.Single(RunOnControls(("btn-salvar", "button")).Diagnostics);

    [Fact]
    public void ScreenWithAccentIsAnError()
    {
        var screen = new Control { Name = "scrConfiguração", TemplateName = "screen", IsScreen = true, Location = Loc("s") };
        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        Assert.Single(new RuleEngine([new SpecialCharacterNameRule()]).Run(project, PpLintConfig.Default).Diagnostics);
    }

    [Fact]
    public void CleanControlNamesAreAccepted() =>
        Assert.Empty(RunOnControls(("btnSalvar", "button"), ("lblTotal_2", "label")).Diagnostics);

    // ---- fluxos ----

    [Fact]
    public void FlowVariableWithAccentIsAnError() =>
        Assert.Single(RunOnFlow(f =>
            f.Variables.Add(new FlowVariable("contagemNúmeros", "integer", Loc("Inicializar")))).Diagnostics);

    [Fact]
    public void FlowActionWithAccentIsAnError() =>
        Assert.Single(RunOnFlow(f =>
            f.Actions.Add(new FlowAction { Name = "Enviar_notificação", Type = "Compose", Location = Loc("a") })).Diagnostics);

    [Fact]
    public void FlowNameWithAccentIsAnError()
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(new CloudFlow { Name = "Rotina_Diária", Location = Loc("F") });

        Assert.Single(new RuleEngine([new SpecialCharacterNameRule()]).Run(project, PpLintConfig.Default).Diagnostics);
    }

    [Fact]
    public void CleanFlowNamesAreAccepted() =>
        Assert.Empty(RunOnFlow(f =>
        {
            f.Variables.Add(new FlowVariable("contagem", "integer", Loc("Inicializar")));
            f.Actions.Add(new FlowAction { Name = "Enviar_email", Type = "Compose", Location = Loc("a") });
        }).Diagnostics);

    // ---- contagem de alvos ----

    [Fact]
    public void EveryNameIsCountedAsATargetNotOnlyTheReported()
    {
        // Contar só o que se reporta faria Evaluated == Violations e a
        // conformidade da regra seria sempre 0% quando ela achasse algo.
        var result = RunOnControls(("btnOk", "button"), ("lblDescrição", "label"));
        var tally = Assert.Single(result.Tallies);

        // Os dois controles mais a tela que os contém.
        Assert.Equal(3, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void TheSameNameIsReportedOnlyOnce()
    {
        // A variável aparece em duas fórmulas; é um nome, um achado — senão a
        // regra reportaria mais violações que alvos.
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var a = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        a.Properties.Add(new PowerFxProperty("OnSelect", "Set(varAção, 1)", Loc("btnA.OnSelect")));
        var b = new Control { Name = "btnB", TemplateName = "button", Location = Loc("btnB") };
        b.Properties.Add(new PowerFxProperty("OnSelect", "Set(varAção, 2)", Loc("btnB.OnSelect")));
        screen.AddChild(a);
        screen.AddChild(b);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        var result = new RuleEngine([new SpecialCharacterNameRule()]).Run(project, PpLintConfig.Default);

        Assert.Single(result.Diagnostics, d => d.Message.Contains("varAção"));
    }

    [Fact]
    public void MessageNamesTheOffendingCharacters()
    {
        // Dizer só "caractere inválido" obriga o usuário a caçar qual. A
        // mensagem precisa apontar o caractere.
        var d = Assert.Single(RunOnControls(("lblDescrição", "label")).Diagnostics);

        Assert.Contains("ç", d.Message);
        Assert.Contains("ã", d.Message);
    }
}
