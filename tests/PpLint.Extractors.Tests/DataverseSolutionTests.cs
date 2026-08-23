using PpLint.Core.Rules;
using PpLint.Rules.Naming;

namespace PpLint.Extractors.Tests;

/// <summary>
/// Extração de tabelas contra uma solução real do Dataverse. O caminho de
/// Entities/ existe desde a Fase 1 e nunca tinha visto um artefato de verdade —
/// que é onde os defeitos aparecem.
/// </summary>
public class DataverseSolutionTests
{
    private static string FixtureDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures"));

    private static string Solucao => Path.Combine(FixtureDir, "solucao-dataverse");

    [Fact]
    public void ReadsTheThreeTables()
    {
        var project = ProjectLoader.Load(Solucao);

        Assert.Equal(
            ["gmx_expense", "gmx_expensecategory", "gmx_expensereport"],
            project.Tables.Select(t => t.LogicalName).Order());
    }

    [Fact]
    public void ReadsColumnsOfEachTable()
    {
        var project = ProjectLoader.Load(Solucao);
        var expense = project.Tables.Single(t => t.LogicalName == "gmx_expense");

        Assert.True(expense.Columns.Count > 20, $"esperava dezenas de colunas, veio {expense.Columns.Count}");
        Assert.Contains(expense.Columns, c => c.SchemaName == "gmx_Amount");
    }

    [Fact]
    public void ReadsThePublisherPrefixFromUnpackedLayout()
    {
        // O pac solution unpack grava Other/Solution.xml, não solution.xml na
        // raiz — e é esse o formato que os times versionam no repositório.
        var project = ProjectLoader.Load(Solucao);

        Assert.Equal("gmx", project.Solution!.PublisherPrefix);
        Assert.Equal("AutomatedExpenseReporting", project.Solution.UniqueName);
    }

    [Fact]
    public void DistinguishesCustomColumnsFromSystemColumns()
    {
        // Sem esta distinção, uma regra de PascalCase acusaria statecode,
        // statuscode e toda a auditoria — dezenas de falsos positivos.
        var project = ProjectLoader.Load(Solucao);
        var expense = project.Tables.Single(t => t.LogicalName == "gmx_expense");

        Assert.True(expense.Columns.Single(c => c.SchemaName == "gmx_Amount").IsCustom);
        Assert.False(expense.Columns.Single(c => c.SchemaName == "statecode").IsCustom);
        Assert.False(expense.Columns.Single(c => c.SchemaName == "CreatedBy").IsCustom);
    }

    [Fact]
    public void MarksColumnsDerivedFromAnotherColumn()
    {
        // gmx_amount_Base traz IsCustomField=1 apesar de o Dataverse tê-la
        // gerado para o par de moeda. O que a denuncia é CalculationOf.
        var project = ProjectLoader.Load(Solucao);
        var expense = project.Tables.Single(t => t.LogicalName == "gmx_expense");

        Assert.Equal("gmx_Amount", expense.Columns.Single(c => c.SchemaName == "gmx_amount_Base").DerivedFrom);
        Assert.Null(expense.Columns.Single(c => c.SchemaName == "gmx_Amount").DerivedFrom);
    }

    [Fact]
    public void ReadsColumnDisplayName()
    {
        var project = ProjectLoader.Load(Solucao);
        var expense = project.Tables.Single(t => t.LogicalName == "gmx_expense");

        Assert.False(string.IsNullOrWhiteSpace(
            expense.Columns.Single(c => c.SchemaName == "gmx_Amount").DisplayName));
    }

    // ---- as regras contra o artefato ----

    [Fact]
    public void NM020_FindsTheColumnFromAnotherPublisher()
    {
        // crfbf_ExpenseReport foi criada com o publisher default do ambiente, e
        // não com o 'gmx' da solução. Achado conferido no Entity.xml.
        var project = ProjectLoader.Load(Solucao);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        var achados = result.Diagnostics.Where(d => d.RuleId == "NM020").ToList();

        Assert.Single(achados);
        Assert.Contains("crfbf_ExpenseReport", achados[0].Message);
    }

    [Fact]
    public void NM021_DoesNotFlagGeneratedOrSystemColumns()
    {
        // statecode e statuscode são do sistema; gmx_amount_Base é derivada.
        // Nenhuma das três foi escrita por alguém.
        var project = ProjectLoader.Load(Solucao);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        var mensagens = result.Diagnostics.Where(d => d.RuleId == "NM021").Select(d => d.Message).ToList();

        Assert.DoesNotContain(mensagens, m => m.Contains("statecode"));
        Assert.DoesNotContain(mensagens, m => m.Contains("statuscode"));
        Assert.DoesNotContain(mensagens, m => m.Contains("_Base"));
        Assert.DoesNotContain(mensagens, m => m.Contains("CreatedBy"));
    }

    [Fact]
    public void EveryTableRuleCountsTargetsWithoutBreakingTheInvariant()
    {
        var project = ProjectLoader.Load(Solucao);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        Assert.All(result.Tallies, t => Assert.True(
            t.Violations <= t.Evaluated,
            $"{t.RuleId} reportou {t.Violations} violações em {t.Evaluated} alvos"));
    }
}

/// <summary>
/// O mesmo conteúdo no formato que a exportação produz: tabelas dentro de
/// customizations.xml, e solution.xml na raiz. É o caminho que o usuário aciona
/// quando exporta a solução do portal.
/// </summary>
public class ExportedSolutionTablesTests
{
    private static string Zip => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "solucao-dataverse-exportada.zip"));

    [Fact]
    public void ReadsTablesFromCustomizationsXml()
    {
        var project = ProjectLoader.Load(Zip);

        Assert.Equal(3, project.Tables.Count);
        Assert.Contains(project.Tables, t => t.SchemaName == "gmx_Expense");
    }

    [Fact]
    public void FindsTheSameIssuesAsTheUnpackedLayout()
    {
        // O pac solution unpack só move o XML para arquivos. Se os dois formatos
        // divergissem nos achados, um dos dois caminhos estaria lendo errado.
        var exportada = ProjectLoader.Load(Zip);
        var descompactada = ProjectLoader.Load(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "solucao-dataverse")));

        var engine = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly);

        static IEnumerable<string> Ids(PpLint.Core.Rules.LintResult r) =>
            r.Diagnostics.Select(d => $"{d.RuleId}: {d.Message}").Order();

        Assert.Equal(
            Ids(engine.Run(descompactada, PpLint.Core.PpLintConfig.Default)),
            Ids(engine.Run(exportada, PpLint.Core.PpLintConfig.Default)));
    }

    [Fact]
    public void ColumnMetadataSurvivesTheExportedLayout()
    {
        var project = ProjectLoader.Load(Zip);
        var expense = project.Tables.Single(t => t.SchemaName == "gmx_Expense");

        Assert.True(expense.Columns.Single(c => c.SchemaName == "gmx_Amount").IsCustom);
        Assert.Equal("gmx_Amount", expense.Columns.Single(c => c.SchemaName == "gmx_amount_Base").DerivedFrom);
    }
}
