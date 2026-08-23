using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Performance;

namespace PpLint.Rules.Tests;

public class StartupRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Properties.json", s, 0, 0);

    private static LintResult Run(IRule rule, string onStart, params string[] fontesExternas)
    {
        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.AppProperties.Add(new PowerFxProperty("OnStart", onStart, Loc("App.OnStart")));

        foreach (var f in fontesExternas)
            app.DataSources.Add(new DataSource(f, "ServiceInfo", []));

        app.DataSources.Add(new DataSource("colLocal", "CollectionDataSourceInfo", []));

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void PERF402_ReportsSequentialNetworkCalls()
    {
        var d = Assert.Single(Run(new SequentialStartupRule(),
            "ClearCollect(colA, Pedidos); ClearCollect(colB, Clientes); ClearCollect(colC, Produtos)",
            "Pedidos", "Clientes", "Produtos").Diagnostics);

        Assert.Equal("PERF402", d.RuleId);
    }

    [Fact]
    public void PERF402_IgnoresCallsOverCollectionsAndLiterals()
    {
        // ClearCollect sobre tabela literal ou sobre outra coleção não vai à
        // rede. Contá-las produziria o conselho "paralelize" para um OnStart
        // que não espera por nada.
        Assert.Empty(Run(new SequentialStartupRule(),
            "ClearCollect(colA, [1,2,3]); ClearCollect(colB, [4,5]); ClearCollect(colC, colA); "
            + "ClearCollect(colD, Filter(colB, Value > 1))").Diagnostics);
    }

    [Fact]
    public void PERF402_AcceptsCallsAlreadyInsideConcurrent() =>
        Assert.Empty(Run(new SequentialStartupRule(),
            "Concurrent(ClearCollect(colA, Pedidos), ClearCollect(colB, Clientes), ClearCollect(colC, Produtos))",
            "Pedidos", "Clientes", "Produtos").Diagnostics);

    [Fact]
    public void PERF402_AcceptsTwoCalls() =>
        Assert.Empty(Run(new SequentialStartupRule(),
            "ClearCollect(colA, Pedidos); ClearCollect(colB, Clientes)", "Pedidos", "Clientes").Diagnostics);

    [Fact]
    public void PERF403_ReportsStartupOverBudget()
    {
        var fontes = Enumerable.Range(1, 9).Select(i => $"Tabela{i}").ToArray();
        var script = string.Join("; ", fontes.Select((f, i) => $"ClearCollect(col{i}, {f})"));

        Assert.Single(Run(new HeavyStartupRule(), script, fontes).Diagnostics);
    }

    [Fact]
    public void PERF403_ConcurrentDoesNotReduceTheVolume()
    {
        // Paralelizar reduz o tempo, não o volume que desce antes da primeira
        // tela: o orçamento continua estourado.
        var fontes = Enumerable.Range(1, 9).Select(i => $"Tabela{i}").ToArray();
        var script = "Concurrent(" + string.Join(", ", fontes.Select((f, i) => $"ClearCollect(col{i}, {f})")) + ")";

        Assert.Single(Run(new HeavyStartupRule(), script, fontes).Diagnostics);
    }

    [Fact]
    public void PERF403_AcceptsModestStartup() =>
        Assert.Empty(Run(new HeavyStartupRule(),
            "ClearCollect(colA, Pedidos); Set(varUsuario, User())", "Pedidos").Diagnostics);

    [Fact]
    public void OnStartIsCountedEvenWhenItPasses()
    {
        var result = Run(new HeavyStartupRule(), "Set(varX, 1)");

        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }
}

public class DelegationRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(string script, params string[] externas)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var c = new Control { Name = "gal", TemplateName = "gallery", Location = Loc("gal") };
        c.Properties.Add(new PowerFxProperty("Items", script, Loc("gal.Items")));
        screen.AddChild(c);

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);
        foreach (var e in externas)
            app.DataSources.Add(new DataSource(e, "ServiceInfo", []));
        app.DataSources.Add(new DataSource("colItens", "CollectionDataSourceInfo", []));

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new DelegationRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsSearchOverExternalSource()
    {
        var d = Assert.Single(Run("Search(Pedidos, txtBusca.Text, \"Titulo\")", "Pedidos").Diagnostics);

        Assert.Equal("PERF401", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("Pedidos", d.Message);
    }

    [Fact]
    public void AcceptsDelegableFunction() =>
        Assert.Empty(Run("Filter(Pedidos, Status = \"Aberto\")", "Pedidos").Diagnostics);

    [Fact]
    public void IgnoresCollections()
    {
        // Coleção vive na memória e nunca foi delegável; cobrá-la seria pedir
        // o impossível.
        Assert.Empty(Run("Search(colItens, \"x\", \"Titulo\")", "Pedidos").Diagnostics);
    }

    [Fact]
    public void SeesTheSourceThroughNestedTableFunctions() =>
        Assert.Single(Run("First(Sort(Pedidos, Data))", "Pedidos").Diagnostics);

    [Fact]
    public void EveryCallOverAnExternalSourceIsATarget()
    {
        var result = Run("Filter(Pedidos, Ativo)", "Pedidos");

        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
        Assert.Equal(0, result.Tallies[0].Violations);
    }
}
