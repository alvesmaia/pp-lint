using System.Reflection;
using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Rules;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Naming;
using static PpLint.Rules.Tests.RuleTestHarness;

namespace PpLint.Rules.Tests;

public class RuleCatalogContractTests
{
    private static readonly Assembly RulesAssembly = typeof(DefaultControlNameRule).Assembly;

    private static IEnumerable<(Type Type, RuleAttribute Meta)> Catalog() =>
        RulesAssembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IRule).IsAssignableFrom(t))
            .Select(t => (Type: t, Meta: t.GetCustomAttribute<RuleAttribute>()!))
            .Where(x => x.Meta is not null);

    [Fact]
    public void EveryRuleImplementationDeclaresTheRuleAttribute()
    {
        var missing = RulesAssembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IRule).IsAssignableFrom(t)
                        && t.GetCustomAttribute<RuleAttribute>() is null)
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void RuleIdsAreUnique()
    {
        var duplicated = Catalog()
            .GroupBy(x => x.Meta.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicated);
    }

    [Fact]
    public void RuleIdsFollowThePrefixConvention()
    {
        var pattern = new Regex("^(NM|PF|FL|DUP|PERF|SEC|SOL)[0-9]{3}$");
        var invalid = Catalog().Where(x => !pattern.IsMatch(x.Meta.Id)).Select(x => x.Meta.Id).ToList();

        Assert.Empty(invalid);
    }

    [Fact]
    public void RuleIdPrefixMatchesItsCategory()
    {
        var expected = new Dictionary<string, RuleCategory>
        {
            ["NM"] = RuleCategory.Naming,
            ["PF"] = RuleCategory.PowerFx,
            ["FL"] = RuleCategory.Flow,
            ["DUP"] = RuleCategory.Duplication,
            ["PERF"] = RuleCategory.Performance,
            ["SEC"] = RuleCategory.Security,
            ["SOL"] = RuleCategory.Solution,
        };

        foreach (var (type, meta) in Catalog())
        {
            var prefix = expected.Keys.First(p => meta.Id.StartsWith(p, StringComparison.Ordinal));
            Assert.True(
                expected[prefix] == meta.Category,
                $"{type.Name}: o ID {meta.Id} não corresponde à categoria {meta.Category}.");
        }
    }

    [Fact]
    public void EveryRuleHasAParameterlessConstructor()
    {
        var invalid = Catalog()
            .Where(x => x.Type.GetConstructor(Type.EmptyTypes) is null)
            .Select(x => x.Type.FullName)
            .ToList();

        Assert.Empty(invalid);
    }

    [Fact]
    public void EveryRuleCountsEvaluatedTargetsOnARepresentativeProject()
    {
        // Projeto com um app e um fluxo, ambos com conteúdo: nenhuma regra
        // desta fase pode terminar sem ter examinado alvo algum.
        // A fórmula precisa exercitar cada forma que as regras procuram: variável
        // de cada tipo, If de três argumentos, If aninhado no ramo senão,
        // comparação booleana, negação, Filter, CountRows e concatenação.
        var app = App("A", Screen("scrHome",
            Ctl("btnSalvar", "button",
                ("OnSelect",
                 "Set(varTotal, 1); UpdateContext({locAberto: true}); ClearCollect(colItens, [1]); "
                 + "If(varTotal > 0, Notify(\"ok\")); "
                 + "Set(varA, If(locAberto, 1, If(varTotal > 0, 2, 3))); "
                 + "Set(varB, If(locAberto = true, Not(varTotal > 0), false)); "
                 + "Set(varC, CountRows(Filter(colItens, varTotal > 0)) > 0); "
                 + "Set(varD, Text(varTotal) & \"x\")"))));

        var flow = new CloudFlow
        {
            Name = "F",
            Description = "fluxo de exemplo",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "F", 0, 0),
        };
        flow.Trigger = new FlowTrigger(
            "R", "Recurrence",
            new SourceLocation("teste.zip", "Workflows/f.json", "R", 0, 0),
            new FlowRecurrence("Day", 1));

        var inicializa = new FlowAction
        {
            Name = "Inicializar",
            Type = "InitializeVariable",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "Inicializar", 0, 0),
        };
        inicializa.RunAfterStates.Add("Succeeded");

        var compoe = new FlowAction
        {
            Name = "Compor",
            Type = "Compose",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "Compor", 0, 0),
        };
        compoe.RunAfter.Add("Inicializar");
        compoe.RunAfterStates.Add("Succeeded");
        compoe.Expressions.Add("@{variables('varX')}");

        var laco = new FlowAction
        {
            Name = "Laco",
            Type = "Foreach",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "Laco", 0, 0),
        };
        laco.RunAfterStates.Add("Succeeded");

        flow.Actions.Add(inicializa);
        flow.Actions.Add(compoe);
        flow.Actions.Add(laco);
        flow.Variables.Add(new FlowVariable("varX", "integer",
            new SourceLocation("teste.zip", "Workflows/f.json", "Inicializar", 0, 0)));


        // Uma tabela com coluna criada por alguém, senão as regras de coluna não
        // têm alvo. O manifesto precisa vir junto: a NM020 compara com o
        // publisher e desiste quando não há nenhum declarado.
        var tabela = new DataTable(
            "gmx_pedido",
            "gmx_Pedido",
            [
                new DataColumn("gmx_valor", "gmx_Valor", "money", false)
                {
                    IsCustom = true,
                    DisplayName = "Valor",
                },
            ],
            new SourceLocation("teste.zip", "Entities/gmx_Pedido/Entity.xml", "gmx_pedido", 0, 0));

        var project = ProjectWith(app);
        project.Flows.Add(flow);
        project.Tables.Add(tabela);
        project.Solution = new SolutionInfo("Teste", "gmx", "1.0.0.0", Managed: false);

        var result = RuleEngine.CreateDefault(RulesAssembly).Run(project, PpLintConfig.Default);

        var silent = result.Tallies.Where(t => t.Evaluated == 0).Select(t => t.RuleId).ToList();
        Assert.Empty(silent);
    }

    [Fact]
    public void NoRuleReportsMoreViolationsThanTargetsEvaluated()
    {
        var app = App("A", Screen("Screen1",
            Ctl("Button1", "button", ("OnSelect", "Set(varNaoUsada, 1); If(2 > 1, Notify(\"x\"))")),
            Ctl("SemPrefixo", "label", ("Text", "\"a\""))));

        var flow = new CloudFlow
        {
            Name = "F",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "F", 0, 0),
        };
        flow.Variables.Add(new FlowVariable("varY", "integer",
            new SourceLocation("teste.zip", "Workflows/f.json", "Inicializar", 0, 0)));

        var project = ProjectWith(app);
        project.Flows.Add(flow);

        var result = RuleEngine.CreateDefault(RulesAssembly).Run(project, PpLintConfig.Default);

        foreach (var tally in result.Tallies)
        {
            Assert.True(
                tally.Violations <= tally.Evaluated,
                $"{tally.RuleId} violou o invariante do índice: {tally.Violations} > {tally.Evaluated}.");
        }
    }

    [Fact]
    public void CatalogContainsThePhaseOneRules()
    {
        var ids = Catalog().Select(x => x.Meta.Id).ToList();
        Assert.Contains("NM010", ids);
        Assert.Contains("NM011", ids);
        Assert.Contains("PF101", ids);
        Assert.Contains("PF110", ids);
        Assert.Contains("FL201", ids);
    }
    [Fact]
    public void EveryRuleInTheCatalogHasDocumentation()
    {
        // Regra sem documento faz 'pp-lint explain' e o SARIF sa�rem mancos
        // justamente para a regra nova, que � a que ningu�m conhece.
        var semDoc = RulesAssembly.GetTypes()
            .Select(t => t.GetCustomAttributes(typeof(RuleAttribute), false).FirstOrDefault())
            .OfType<RuleAttribute>()
            .Select(a => a.Id)
            .Where(id => RuleDocs.Find(id) is null)
            .Order()
            .ToList();

        Assert.Empty(semDoc);
    }

    [Fact]
    public void EveryDocumentCorrespondsToARuleInTheCatalog()
    {
        // O contr�rio tamb�m: documento �rf�o significa regra removida ou ID
        // digitado errado no nome do arquivo.
        var ids = RulesAssembly.GetTypes()
            .Select(t => t.GetCustomAttributes(typeof(RuleAttribute), false).FirstOrDefault())
            .OfType<RuleAttribute>()
            .Select(a => a.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orfaos = RuleDocs.AvailableIds().Where(id => !ids.Contains(id)).ToList();

        Assert.Empty(orfaos);
    }
}
