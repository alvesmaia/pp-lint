using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL210 — nenhuma ação do fluxo trata falha. Quando algo dá errado, o fluxo
/// morre em silêncio: ninguém é avisado, e o problema aparece dias depois como
/// dado faltando. Basta uma ação com 'Configurar execução após' cobrindo
/// falha, ou um escopo de tratamento.
/// </summary>
[Rule("FL210", RuleCategory.Flow, Severity.Error)]
public sealed class NoErrorHandlingRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            // Fluxo com uma ação só não encadeia nada; não há o que tratar.
            if (flow.AllActions().Count() < 2)
                continue;

            ctx.Evaluated(1);

            if (!FlowExecutionGraph.Build(flow).HandlesFailure)
            {
                ctx.Report(
                    flow.Location,
                    $"O fluxo '{flow.Name}' não trata falha em nenhuma ação. "
                    + "Configure 'Executar após' com falha em alguma etapa, ou envolva o "
                    + "trecho crítico num escopo com tratamento.");
            }
        }
    }
}
