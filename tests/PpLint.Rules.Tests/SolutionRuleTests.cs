using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Solution;

namespace PpLint.Rules.Tests;

public class SolutionRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Entities/t/Entity.xml", s, 0, 0);

    private static PowerPlatformProject Projeto(string prefixo, bool managed, params string[] tabelas)
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Solution = new SolutionInfo("Sol", prefixo, "1.0.0.0", managed);

        foreach (var t in tabelas)
            project.Tables.Add(new DataTable(t.ToLowerInvariant(), t, [], Loc(t)));

        return project;
    }

    [Fact]
    public void SOL601_ReportsTableFromAnotherPublisher()
    {
        var d = Assert.Single(new RuleEngine([new TablePublisherPrefixRule()])
            .Run(Projeto("gmx", false, "cr1a2_Pedido"), PpLintConfig.Default).Diagnostics);

        Assert.Equal("SOL601", d.RuleId);
        Assert.Contains("cr1a2_Pedido", d.Message);
    }

    [Fact]
    public void SOL601_AcceptsTableWithTheSolutionPrefix() =>
        Assert.Empty(new RuleEngine([new TablePublisherPrefixRule()])
            .Run(Projeto("gmx", false, "gmx_Pedido"), PpLintConfig.Default).Diagnostics);

    [Fact]
    public void SOL601_IgnoresSystemTables()
    {
        // account e contact já vêm no ambiente e não pertencem a publisher
        // nenhum: cobrá-las seria pedir para renomear a plataforma.
        Assert.Empty(new RuleEngine([new TablePublisherPrefixRule()])
            .Run(Projeto("gmx", false, "account", "contact"), PpLintConfig.Default).Diagnostics);
    }

    [Fact]
    public void SOL603_ReportsManagedSolution()
    {
        var d = Assert.Single(new RuleEngine([new ManagedSolutionRule()])
            .Run(Projeto("gmx", managed: true), PpLintConfig.Default).Diagnostics);

        Assert.Equal("SOL603", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void SOL603_AcceptsUnmanagedSolution() =>
        Assert.Empty(new RuleEngine([new ManagedSolutionRule()])
            .Run(Projeto("gmx", managed: false), PpLintConfig.Default).Diagnostics);
}

public class UnknownColumnRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static LintResult Run(string expressao, params string[] colunas)
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Tables.Add(new DataTable(
            "gmx_pedido",
            "gmx_Pedido",
            colunas.Select(c => new DataColumn(c.ToLowerInvariant(), c, "nvarchar", false)).ToList(),
            Loc("gmx_pedido")));

        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        var acao = new FlowAction { Name = "Compor", Type = "Compose", Location = Loc("Compor") };
        acao.Expressions.Add(expressao);
        flow.Actions.Add(acao);
        project.Flows.Add(flow);

        return new RuleEngine([new UnknownColumnRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsColumnThatDoesNotExist()
    {
        var d = Assert.Single(Run("@{gmx_pedido?['gmx_valorr']}", "gmx_Valor").Diagnostics);

        Assert.Equal("SOL602", d.RuleId);
    }

    [Fact]
    public void AcceptsExistingColumn() =>
        Assert.Empty(Run("@{gmx_pedido?['gmx_valor']}", "gmx_Valor").Diagnostics);

    [Fact]
    public void StaysSilentAboutTablesItCannotSee()
    {
        // Uma lista do SharePoint fora da solução não tem esquema extraído;
        // acusar suas colunas seria inventar.
        Assert.Empty(Run("@{outra_tabela?['Titulo']}", "gmx_Valor").Diagnostics);
    }
}
