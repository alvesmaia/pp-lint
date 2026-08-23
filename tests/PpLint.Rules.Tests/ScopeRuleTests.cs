using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class ContextVariableInOnStartTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Properties.json", s, 0, 0);

    private static LintResult Run(string onStart)
    {
        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.AppProperties.Add(new PowerFxProperty("OnStart", onStart, Loc("App.OnStart")));

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new ContextVariableInOnStartRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsUpdateContextInOnStart()
    {
        var d = Assert.Single(Run("UpdateContext({locX: 1})").Diagnostics);

        Assert.Equal("PF120", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void AcceptsSetInOnStart() =>
        Assert.Empty(Run("Set(varUsuario, User())").Diagnostics);

    [Fact]
    public void OnStartIsCountedEvenWhenItPasses()
    {
        var result = Run("Set(varX, 1)");

        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }
}

public class CrossScreenReferenceTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static Control Screen(string nome, params (string Ctl, string Prop, string Script)[] controles)
    {
        var s = new Control { Name = nome, TemplateName = "screen", IsScreen = true, Location = Loc(nome) };

        foreach (var (ctl, prop, script) in controles)
        {
            var existente = s.Children.FirstOrDefault(c => c.Name == ctl);
            if (existente is null)
            {
                existente = new Control { Name = ctl, TemplateName = "label", Location = Loc(ctl) };
                s.AddChild(existente);
            }

            if (prop.Length > 0)
                existente.Properties.Add(new PowerFxProperty(prop, script, Loc($"{ctl}.{prop}")));
        }

        return s;
    }

    private static LintResult Run(params Control[] telas)
    {
        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.AddRange(telas);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new CrossScreenReferenceRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsReadingAControlFromAnotherScreen()
    {
        var d = Assert.Single(Run(
            Screen("scrConfig", ("sldTempo", "", "")),
            Screen("scrJogo", ("lblTempo", "Text", "sldTempo.Value"))).Diagnostics);

        Assert.Equal("PF121", d.RuleId);
        Assert.Contains("sldTempo", d.Message);
        Assert.Contains("scrConfig", d.Message);
    }

    [Fact]
    public void AcceptsReadingAControlOnTheSameScreen() =>
        Assert.Empty(Run(
            Screen("scrJogo", ("sldTempo", "", ""), ("lblTempo", "Text", "sldTempo.Value"))).Diagnostics);

    [Fact]
    public void AcceptsReadingAScreenDimension()
    {
        // 'scrConfig'.Width é referência de layout: resolve sem a tela estar
        // carregada. O que não resolve é o valor guardado num controle dela.
        Assert.Empty(Run(
            Screen("scrConfig", ("lbl", "", "")),
            Screen("scrJogo", ("lblA", "Width", "scrConfig.Width"))).Diagnostics);
    }

    [Fact]
    public void AcceptsReadingAVariable() =>
        Assert.Empty(Run(
            Screen("scrConfig", ("sldTempo", "", "")),
            Screen("scrJogo", ("lblTempo", "Text", "varTempo"))).Diagnostics);

    [Fact]
    public void EveryControlReferenceIsATarget()
    {
        var result = Run(
            Screen("scrConfig", ("sldTempo", "", "")),
            Screen("scrJogo", ("sldLocal", "", ""), ("lblA", "Text", "sldLocal.Value"), ("lblB", "Width", "sldTempo.Value")));

        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}

public class UnparsableFormulaTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var c = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        c.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(c);

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new UnparsableFormulaRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsBrokenFormula()
    {
        var d = Assert.Single(Run("Set(varX, ").Diagnostics);

        Assert.Equal("PF130", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void AcceptsValidFormula() =>
        Assert.Empty(Run("Set(varX, 1)").Diagnostics);

    [Fact]
    public void MessageExplainsTheConsequence()
    {
        // Fórmula que não compila é pulada por toda regra de Power Fx: o usuário
        // precisa saber que aquele trecho não foi examinado por nenhuma outra.
        var d = Assert.Single(Run("Set(varX, ").Diagnostics);

        Assert.Contains("não foi examinado", d.Message);
    }
}
