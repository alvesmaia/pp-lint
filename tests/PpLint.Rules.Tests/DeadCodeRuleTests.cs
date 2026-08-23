using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Duplication;

namespace PpLint.Rules.Tests;

public class UnusedDataSourceRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(DataSource source, params string[] formulas)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };

        for (var i = 0; i < formulas.Length; i++)
        {
            var c = new Control { Name = $"btn{i}", TemplateName = "button", Location = Loc($"btn{i}") };
            c.Properties.Add(new PowerFxProperty("OnSelect", formulas[i], Loc($"btn{i}.OnSelect")));
            screen.AddChild(c);
        }

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);
        app.DataSources.Add(source);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new UnusedDataSourceRule()]).Run(project, PpLintConfig.Default);
    }

    private static DataSource Externa(string nome) => new(nome, "ServiceInfo", []);

    [Fact]
    public void ReportsSourceNeverMentioned() =>
        Assert.Single(Run(Externa("Pedidos"), "Set(varX, 1)").Diagnostics);

    [Fact]
    public void AcceptsSourceUsedAsFunctionQualifier()
    {
        // Office365Users.UserPhotoV(...) usa a fonte como qualificador — ela é o
        // lado ESQUERDO do ponto. Um índice que só olhasse o lado direito
        // acusaria um conector legitimamente em uso, que foi o que aconteceu
        // com o app real.
        Assert.Empty(Run(Externa("Office365Users"),
            "Set(varFoto, Office365Users.UserPhotoV2(varEmail))").Diagnostics);
    }

    [Fact]
    public void AcceptsSourceUsedDirectly() =>
        Assert.Empty(Run(Externa("Pedidos"), "ClearCollect(colP, Filter(Pedidos, Ativo))").Diagnostics);

    [Fact]
    public void IgnoresCollections()
    {
        // Coleção morta é assunto da PF103, que sabe onde ela foi criada.
        Assert.Empty(Run(new DataSource("colBoard", "CollectionDataSourceInfo", []), "Set(varX, 1)").Diagnostics);
    }

    [Fact]
    public void IgnoresStudioSampleData() =>
        Assert.Empty(Run(new DataSource("ComboBoxSample", "StaticDataSourceInfo", []), "Set(varX, 1)").Diagnostics);
}

public class UnreachableScreenRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(string[] telas, params string[] formulas)
    {
        var app = new CanvasApp { Name = "App", Location = Loc("App") };

        foreach (var nome in telas)
            app.Screens.Add(new Control { Name = nome, TemplateName = "screen", IsScreen = true, Location = Loc(nome) });

        for (var i = 0; i < formulas.Length; i++)
        {
            var c = new Control { Name = $"btn{i}", TemplateName = "button", Location = Loc($"btn{i}") };
            c.Properties.Add(new PowerFxProperty("OnSelect", formulas[i], Loc($"btn{i}.OnSelect")));
            app.Screens[0].AddChild(c);
        }

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new UnreachableScreenRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsScreenNobodyNavigatesTo()
    {
        var d = Assert.Single(Run(["scrHome", "scrOrfa"], "Set(varX, 1)").Diagnostics);

        Assert.Equal("DUP304", d.RuleId);
        Assert.Contains("scrOrfa", d.Message);
    }

    [Fact]
    public void AcceptsScreenReachedByNavigate() =>
        Assert.Empty(Run(["scrHome", "scrDetalhe"], "Navigate(scrDetalhe)").Diagnostics);

    [Fact]
    public void FirstScreenIsNeverReported()
    {
        // A primeira tela é a que abre com o app; ninguém navega para ela.
        // Exigir isso acusaria todo app existente.
        Assert.Empty(Run(["scrHome"], "Set(varX, 1)").Diagnostics);
    }

    [Fact]
    public void AcceptsScreenNameWithSpaceQuotedInNavigate()
    {
        // Nome com espaço obriga aspas simples na fórmula, e o alvo precisa ser
        // reconhecido mesmo assim — é como o app real navega.
        Assert.Empty(Run(["scrHome", "Game Screen"], "Navigate('Game Screen', ScreenTransition.Fade)").Diagnostics);
    }

    [Fact]
    public void EveryScreenAfterTheFirstIsATarget()
    {
        var result = Run(["scrHome", "scrA", "scrB"], "Navigate(scrA)");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}

public class DeadControlRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(
        (string Name, string? Visible)[] controles, params string[] formulas)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };

        foreach (var (nome, visible) in controles)
        {
            var c = new Control { Name = nome, TemplateName = "label", Location = Loc(nome) };
            if (visible is not null)
                c.Properties.Add(new PowerFxProperty("Visible", visible, Loc($"{nome}.Visible")));
            screen.AddChild(c);
        }

        for (var i = 0; i < formulas.Length; i++)
        {
            var c = new Control { Name = $"btn{i}", TemplateName = "button", Location = Loc($"btn{i}") };
            c.Properties.Add(new PowerFxProperty("OnSelect", formulas[i], Loc($"btn{i}.OnSelect")));
            screen.AddChild(c);
        }

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new DeadControlRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsInvisibleControlNobodyReferences()
    {
        var d = Assert.Single(Run([("lblMorto", "false")]).Diagnostics);

        Assert.Equal("DUP303", d.RuleId);
        Assert.Contains("lblMorto", d.Message);
    }

    [Fact]
    public void AcceptsInvisibleControlThatIsRead()
    {
        // Um controle escondido cujo valor alimenta outra fórmula não é morto —
        // esconder e ler é idioma comum para guardar estado.
        Assert.Empty(Run([("lblCache", "false")], "Set(varX, lblCache.Text)").Diagnostics);
    }

    [Fact]
    public void AcceptsVisibleControl() =>
        Assert.Empty(Run([("lblTitulo", "true")]).Diagnostics);

    [Fact]
    public void AcceptsControlWithoutVisibleProperty() =>
        Assert.Empty(Run([("lblTitulo", null)]).Diagnostics);

    [Fact]
    public void AcceptsControlWhoseVisibilityIsAnExpression()
    {
        // A expressão pode ser verdadeira em algum momento; chamar de morto
        // seria errado.
        Assert.Empty(Run([("lblErro", "varTemErro")]).Diagnostics);
    }

    [Fact]
    public void OnlyInvisibleControlsAreTargets()
    {
        // Contar todo controle do app inflaria o denominador com centenas de
        // alvos que a regra nunca examinaria de verdade.
        var result = Run([("lblA", "true"), ("lblB", "false")]);

        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }
}
