using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Duplication;

namespace PpLint.Rules.Tests;

public class DuplicateFormulaRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(params (string Property, string Script)[] formulas)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };

        for (var i = 0; i < formulas.Length; i++)
        {
            var c = new Control { Name = $"ctl{i}", TemplateName = "button", Location = Loc($"ctl{i}") };
            c.Properties.Add(new PowerFxProperty(formulas[i].Property, formulas[i].Script, Loc($"ctl{i}")));
            screen.AddChild(c);
        }

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new DuplicateFormulaRule()]).Run(project, PpLintConfig.Default);
    }

    private const string Longa =
        "If(varUsuario.Perfil = \"Admin\", Navigate(scrAdmin), Navigate(scrPadrao))";

    [Fact]
    public void ReportsFormulaRepeatedThreeTimes()
    {
        var d = Assert.Single(Run(
            ("OnSelect", Longa), ("OnSelect", Longa), ("OnSelect", Longa)).Diagnostics);

        Assert.Equal("DUP301", d.RuleId);
        Assert.Contains("3 vezes", d.Message);
    }

    [Fact]
    public void AcceptsFormulaRepeatedTwice()
    {
        // Duas ocorrências costumam ser coincidência; três é padrão.
        Assert.Empty(Run(("OnSelect", Longa), ("OnSelect", Longa)).Diagnostics);
    }

    [Fact]
    public void IgnoresShortFormulas()
    {
        // 'Parent.Fill' repete-se por toda parte e extrair não melhoraria nada.
        Assert.Empty(Run(
            ("Fill2", "Parent.Fill"), ("Fill2", "Parent.Fill"), ("Fill2", "Parent.Fill")).Diagnostics);
    }

    [Fact]
    public void IgnoresStudioGeneratedProperties()
    {
        // Size e Orientation nascem idênticas em toda tela criada pelo Studio —
        // no app real elas aparecem 8 vezes cada, e cobrá-las seria reclamar de
        // código que ninguém escreveu.
        Assert.Empty(Run(
            ("Size", "If(Self.Width < Self.Height, Layout.Vertical, Layout.Horizontal)"),
            ("Size", "If(Self.Width < Self.Height, Layout.Vertical, Layout.Horizontal)"),
            ("Size", "If(Self.Width < Self.Height, Layout.Vertical, Layout.Horizontal)")).Diagnostics);
    }

    [Fact]
    public void IgnoresRepeatedSubexpressions()
    {
        // Min(viewBox.Width, viewBox.Height) aparece 66 vezes no app real dentro
        // de fórmulas diferentes. É uso normal de um valor, não duplicação.
        Assert.Empty(Run(
            ("Text", "Min(viewBox.Width, viewBox.Height) * 50% + varOffsetSuperior"),
            ("Text", "Min(viewBox.Width, viewBox.Height) * 40% + varOffsetLateral"),
            ("Text", "Min(viewBox.Width, viewBox.Height) * 25% + varOffsetInferior")).Diagnostics);
    }

    [Fact]
    public void FormattingDifferencesStillCountAsTheSameFormula()
    {
        var comEspaco = "If(varUsuario.Perfil = \"Admin\" , Navigate(scrAdmin) , Navigate(scrPadrao))";

        Assert.Single(Run(
            ("OnSelect", Longa), ("OnSelect", comEspaco), ("OnSelect", Longa)).Diagnostics);
    }

    [Fact]
    public void EveryDistinctFormulaIsATarget()
    {
        // A maioria das fórmulas não se repete, e é isso que faz a conformidade
        // desta regra significar algo.
        var result = Run(
            ("OnSelect", Longa),
            ("OnSelect", Longa),
            ("OnSelect", Longa),
            ("OnSelect", "Set(varOutraCoisaBemDiferente, CountRows(colPedidosAbertos))"));

        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void MessageNamesWhereTheCopiesAre()
    {
        // Sem os nomes, o usuário sabe que há duplicação e não sabe onde.
        var d = Assert.Single(Run(
            ("OnSelect", Longa), ("OnSelect", Longa), ("OnSelect", Longa)).Diagnostics);

        Assert.Contains("ctl0.OnSelect", d.Message);
        Assert.Contains("ctl1.OnSelect", d.Message);
    }
}
