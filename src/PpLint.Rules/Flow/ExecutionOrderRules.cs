using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL202 — uma ação lê a variável antes da ação que a inicializa. No Power
/// Automate isso é erro de execução, não valor vazio.
///
/// Só acusa quando a ordem é conhecida e está invertida: ações em ramos
/// independentes não têm ordem no formato, e chutar geraria falso positivo.
/// </summary>
[Rule("FL202", RuleCategory.Flow, Severity.Error)]
public sealed class VariableUsedBeforeInitializationRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            var graph = FlowExecutionGraph.Build(flow);
            var acoes = flow.AllActions().ToList();

            foreach (var variable in flow.Variables)
            {
                ctx.Evaluated(1);

                var inicializadora = acoes.FirstOrDefault(a =>
                    a.Type.Equals("InitializeVariable", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Location.Symbol, variable.Location.Symbol, StringComparison.OrdinalIgnoreCase));

                if (inicializadora is null)
                    continue;

                var leitoraAnterior = acoes.FirstOrDefault(a =>
                    !ReferenceEquals(a, inicializadora)
                    && LeVariavel(a, variable.Name)
                    && graph.RunsBefore(a.Name, inicializadora.Name));

                if (leitoraAnterior is not null)
                {
                    ctx.Report(
                        leitoraAnterior.Location,
                        $"A ação '{leitoraAnterior.Name}' lê a variável '{variable.Name}' antes de "
                        + $"'{inicializadora.Name}' inicializá-la. No Power Automate isso falha em execução.");
                }
            }
        }
    }

    private static bool LeVariavel(FlowAction action, string nome)
    {
        var padrao = new Regex(
            $@"variables\(\s*'{Regex.Escape(nome)}'\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return action.Expressions.Any(padrao.IsMatch);
    }
}

/// <summary>
/// FL241 — 'Executar após' aponta para uma ação que não existe no fluxo. O
/// arquivo está inconsistente: costuma acontecer quando alguém edita o JSON à
/// mão ou renomeia uma ação sem atualizar quem dependia dela.
/// </summary>
[Rule("FL241", RuleCategory.Flow, Severity.Error)]
public sealed class UnknownPredecessorRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            ctx.Evaluated(1);

            var desconhecidos = FlowExecutionGraph.Build(flow).UnknownPredecessors;

            if (desconhecidos.Count > 0)
            {
                ctx.Report(
                    flow.Location,
                    $"O fluxo '{flow.Name}' depende de ações que não existem: "
                    + $"{string.Join(", ", desconhecidos)}.");
            }
        }
    }
}
