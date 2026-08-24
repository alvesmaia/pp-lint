using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Duplication;
using PpLint.Rules.Flow;
using PpLint.Rules.Naming;

namespace PpLint.Rules.Tests;

public class ConnectorRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static LintResult Run(IRule rule, params FlowAction[] acoes)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.AddRange(acoes);

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    private static FlowAction Consulta(string nome, string operacao, params string[] parametros) =>
        new()
        {
            Name = nome,
            Type = "OpenApiConnection",
            OperationId = operacao,
            ParameterNames = parametros,
            Location = Loc(nome),
        };

    // ---- FL212 ----

    [Fact]
    public void FL212_ReportsActionWithStaticResultEnabled()
    {
        var acao = new FlowAction
        {
            Name = "Gravar",
            Type = "OpenApiConnection",
            StaticResultEnabled = true,
            Location = Loc("Gravar"),
        };

        var d = Assert.Single(Run(new DisabledActionRule(), acao).Diagnostics);

        Assert.Equal("FL212", d.RuleId);
        Assert.Contains("Gravar", d.Message);
    }

    [Fact]
    public void FL212_AcceptsOrdinaryAction() =>
        Assert.Empty(Run(new DisabledActionRule(),
            new FlowAction { Name = "Gravar", Type = "OpenApiConnection", Location = Loc("Gravar") }).Diagnostics);

    // ---- FL220 ----

    [Fact]
    public void FL220_ReportsQueryWithoutFilter()
    {
        var d = Assert.Single(Run(new QueryWithoutFilterRule(),
            Consulta("Obter_itens", "GetItems", "dataset", "table")).Diagnostics);

        Assert.Equal("FL220", d.RuleId);
    }

    [Fact]
    public void FL220_AcceptsQueryWithFilter() =>
        Assert.Empty(Run(new QueryWithoutFilterRule(),
            Consulta("Obter_itens", "GetItems", "dataset", "table", "$filter")).Diagnostics);

    [Fact]
    public void FL220_RecognisesDataverseListRecords() =>
        Assert.Single(Run(new QueryWithoutFilterRule(),
            Consulta("Listar_linhas", "ListRecords", "entityName")).Diagnostics);

    [Fact]
    public void FL220_IgnoresOperationsThatAreNotQueries()
    {
        // Enviar e-mail não tem o que filtrar; a regra precisa saber a
        // diferença, e o tipo da ação diz apenas 'OpenApiConnection' para as
        // duas.
        Assert.Empty(Run(new QueryWithoutFilterRule(),
            Consulta("Enviar", "SendEmailV2", "emailMessage/To")).Diagnostics);
    }

    [Fact]
    public void FL220_OnlyQueriesCountAsTargets()
    {
        var result = Run(new QueryWithoutFilterRule(),
            Consulta("Obter", "GetItems", "dataset"),
            Consulta("Enviar", "SendEmailV2", "to"));

        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }

    // ---- FL221 ----

    [Fact]
    public void FL221_ReportsQueryWithoutTop()
    {
        var d = Assert.Single(Run(new QueryWithoutTopRule(),
            Consulta("Obter_itens", "GetItems", "dataset", "$filter")).Diagnostics);

        Assert.Equal("FL221", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void FL221_AcceptsQueryWithTop() =>
        Assert.Empty(Run(new QueryWithoutTopRule(),
            Consulta("Obter_itens", "GetItems", "dataset", "$filter", "$top")).Diagnostics);

    // ---- FL224 ----

    private static FlowAction Laco(string nome, params FlowAction[] filhos)
    {
        var laco = new FlowAction { Name = nome, Type = "Foreach", Location = Loc(nome) };
        laco.Children.AddRange(filhos);
        return laco;
    }

    private static FlowAction Conector(string nome) =>
        new() { Name = nome, Type = "OpenApiConnection", Location = Loc(nome) };

    [Fact]
    public void FL224_ReportsConnectorInsideLoop()
    {
        var d = Assert.Single(Run(new ConnectorInLoopRule(),
            Laco("Aplicar_a_cada", Conector("Criar_registro"))).Diagnostics);

        Assert.Equal("FL224", d.RuleId);
        Assert.Contains("Criar_registro", d.Message);
        Assert.Contains("Aplicar_a_cada", d.Message);
    }

    [Fact]
    public void FL224_AcceptsConnectorOutsideLoop() =>
        Assert.Empty(Run(new ConnectorInLoopRule(), Conector("Criar_registro")).Diagnostics);

    [Fact]
    public void FL224_IgnoresComposeInsideLoop()
    {
        // Uma composição dentro do laço custa quase nada; o que custa é
        // atravessar a rede a cada item.
        Assert.Empty(Run(new ConnectorInLoopRule(),
            Laco("Aplicar_a_cada", new FlowAction { Name = "Compor", Type = "Compose", Location = Loc("Compor") }))
            .Diagnostics);
    }

    [Fact]
    public void FL224_SeesThroughNesting()
    {
        // O conector pode estar dentro de uma condição dentro do laço.
        var condicao = new FlowAction { Name = "Se", Type = "If", Location = Loc("Se") };
        condicao.Children.Add(Conector("Enviar"));

        Assert.Single(Run(new ConnectorInLoopRule(), Laco("Aplicar_a_cada", condicao)).Diagnostics);
    }

    [Fact]
    public void FL224_EveryConnectorCallIsATarget()
    {
        var result = Run(new ConnectorInLoopRule(),
            Conector("Fora"),
            Laco("Aplicar_a_cada", Conector("Dentro")));

        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}

public class UnusedConnectionRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Connections/Connections.json", s, 0, 0);

    private static LintResult Run(params AppConnection[] conexoes)
    {
        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Connections.AddRange(conexoes);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new UnusedConnectionRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsConnectionWithNothingHangingOffIt()
    {
        var d = Assert.Single(Run(new AppConnection("id1", "Dataverse", "shared_cds", 0, 0)).Diagnostics);

        Assert.Equal("DUP306", d.RuleId);
        Assert.Contains("Dataverse", d.Message);
    }

    [Fact]
    public void AcceptsConnectionWithADataSource() =>
        Assert.Empty(Run(new AppConnection("id1", "Dataverse", "shared_cds", 2, 0)).Diagnostics);

    [Fact]
    public void AcceptsConnectionWithADependentControl()
    {
        // Um conector pode ser usado direto por um controle, sem fonte de dados
        // pendurada — é o caso de Office365Users.
        Assert.Empty(Run(new AppConnection("id1", "Office 365", "shared_o365", 0, 3)).Diagnostics);
    }

    [Fact]
    public void EveryConnectionIsATarget()
    {
        var result = Run(
            new AppConnection("a", "Usada", "x", 1, 0),
            new AppConnection("b", "Esquecida", "y", 0, 0));

        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}

public class ComponentPropertyNamingTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Components/1.json", s, 0, 0);

    private static LintResult Run(params string[] propriedades)
    {
        var componente = new CanvasComponent { Name = "cmpCabecalho", Location = Loc("cmpCabecalho") };
        foreach (var p in propriedades)
            componente.CustomProperties.Add(new ComponentProperty(p, p, "String"));

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Components.Add(componente);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new ComponentPropertyNamingRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsPropertyStartingLowercase()
    {
        // 'cmpHeaderHeight' é um caso real, de um componente publicado.
        var d = Assert.Single(Run("cmpHeaderHeight").Diagnostics);

        Assert.Equal("NM014", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void AcceptsPascalCase() =>
        Assert.Empty(Run("HeaderTitle", "HideShowLangDD").Diagnostics);

    [Fact]
    public void ReportsPropertyWithUnderscore() =>
        Assert.Single(Run("Header_Title").Diagnostics);

    [Fact]
    public void EveryCustomPropertyIsATarget()
    {
        var result = Run("HeaderTitle", "cmpHeaderHeight");

        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
