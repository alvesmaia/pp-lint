using System.Reflection;
using System.Text.RegularExpressions;
using PpLint.Core;
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
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "F", 0, 0),
        };
        flow.Variables.Add(new FlowVariable("varX", "integer",
            new SourceLocation("teste.zip", "Workflows/f.json", "Inicializar", 0, 0)));

        var project = ProjectWith(app);
        project.Flows.Add(flow);

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
}
