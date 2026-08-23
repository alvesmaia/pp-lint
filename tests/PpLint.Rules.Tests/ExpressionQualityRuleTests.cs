using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class ExpressionQualityRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, params (string Prop, string Script)[] formulas)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };

        for (var i = 0; i < formulas.Length; i++)
        {
            var c = new Control { Name = $"ctl{i}", TemplateName = "label", Location = Loc($"ctl{i}") };
            c.Properties.Add(new PowerFxProperty(formulas[i].Prop, formulas[i].Script, Loc($"ctl{i}")));
            screen.AddChild(c);
        }

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    // ---- PF122 ----

    [Fact]
    public void PF122_ReportsCollectInsideForAll()
    {
        var d = Assert.Single(Run(new ForAllWithWriteRule(),
            ("OnSelect", "ForAll(colItens, Collect(Pedidos, {Id: Value}))")).Diagnostics);

        Assert.Equal("PF122", d.RuleId);
        Assert.Contains("Collect", d.Message);
    }

    [Fact]
    public void PF122_ReportsPatchInsideForAll() =>
        Assert.Single(Run(new ForAllWithWriteRule(),
            ("OnSelect", "ForAll(colItens, Patch(Pedidos, LookUp(Pedidos, Id = Value), {Ok: true}))")).Diagnostics);

    [Fact]
    public void PF122_AcceptsForAllThatOnlyComputes() =>
        Assert.Empty(Run(new ForAllWithWriteRule(),
            ("Items", "ForAll(colItens, {Nome: Upper(Titulo)})")).Diagnostics);

    [Fact]
    public void PF122_EveryForAllIsATarget()
    {
        var result = Run(new ForAllWithWriteRule(),
            ("OnSelect", "ForAll(colA, Collect(P, {X: 1}))"),
            ("Items", "ForAll(colB, {Y: 2})"));

        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    // ---- PF123 ----

    [Fact]
    public void PF123_ReportsDeeplyNestedFormula()
    {
        var funda = "Left(Right(Trim(Upper(Lower(Substitute(Text(varX), \"a\", \"b\")))), 3), 2)";
        var d = Assert.Single(Run(new DeepNestingRule(), ("Text", funda)).Diagnostics);

        Assert.Equal("PF123", d.RuleId);
    }

    [Fact]
    public void PF123_AcceptsModestNesting() =>
        Assert.Empty(Run(new DeepNestingRule(), ("Text", "Upper(Trim(varNome))")).Diagnostics);

    [Fact]
    public void PF123_OperatorsDoNotCountAsNesting()
    {
        // Parênteses e operadores aninham a árvore sem aumentar o que custa ler.
        Assert.Empty(Run(new DeepNestingRule(),
            ("Text", "((varA + varB) * (varC - varD)) / ((varE + varF) * (varG - varH))")).Diagnostics);
    }

    // ---- PF124 ----

    [Fact]
    public void PF124_ReportsColorRepeatedManyTimes()
    {
        var cor = "RGBA(0, 120, 212, 1)";
        var formulas = Enumerable.Range(0, 5).Select(_ => ("Fill2", cor)).ToArray();

        var d = Assert.Single(Run(new HardcodedColorRule(), formulas).Diagnostics);

        Assert.Equal("PF124", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
        Assert.Contains("5 vezes", d.Message);
    }

    [Fact]
    public void PF124_AcceptsColorUsedOnce()
    {
        // Cor usada uma vez é escolha local; num app com ilustração vetorial as
        // cores literais chegam às dezenas sem que nada esteja errado.
        Assert.Empty(Run(new HardcodedColorRule(), ("Fill2", "RGBA(0, 120, 212, 1)")).Diagnostics);
    }

    [Fact]
    public void PF124_DifferentColorsDoNotAddUp()
    {
        var formulas = Enumerable.Range(0, 6)
            .Select(i => ("Fill2", $"RGBA({i}, 120, 212, 1)"))
            .ToArray();

        Assert.Empty(Run(new HardcodedColorRule(), formulas).Diagnostics);
    }

    // ---- PF125 ----

    [Fact]
    public void PF125_StaysSilentWhenTheAppHasNoTranslationTable()
    {
        // Em app sem tradução, texto literal é a escolha certa: avisar seria
        // empurrar trabalho que ninguém pediu.
        Assert.Empty(Run(new UntranslatedTextRule(), ("Text", "\"Confirmar pedido\"")).Diagnostics);
    }

    [Fact]
    public void PF125_ReportsLiteralTextWhenTheAppTranslates()
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var c = new Control { Name = "lblTitulo", TemplateName = "label", Location = Loc("lblTitulo") };
        c.Properties.Add(new PowerFxProperty("Text", "\"Confirmar pedido\"", Loc("lblTitulo.Text")));
        screen.AddChild(c);

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);
        app.DataSources.Add(new DataSource("Translations", "ServiceInfo", []));

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        var d = Assert.Single(
            new RuleEngine([new UntranslatedTextRule()]).Run(project, PpLintConfig.Default).Diagnostics);

        Assert.Equal("PF125", d.RuleId);
        Assert.Contains("Confirmar pedido", d.Message);
    }

    [Fact]
    public void PF125_AcceptsTextThatComesFromAFormula()
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var c = new Control { Name = "lblTitulo", TemplateName = "label", Location = Loc("lblTitulo") };
        c.Properties.Add(new PowerFxProperty("Text", "LookUp(Translations, Chave = \"titulo\").Texto", Loc("lblTitulo.Text")));
        screen.AddChild(c);

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);
        app.DataSources.Add(new DataSource("Translations", "ServiceInfo", []));

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        Assert.Empty(new RuleEngine([new UntranslatedTextRule()]).Run(project, PpLintConfig.Default).Diagnostics);
    }
}
