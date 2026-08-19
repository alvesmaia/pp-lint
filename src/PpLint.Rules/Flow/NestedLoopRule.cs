using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL222 — 'Aplicar a cada' dentro de outro. Cem itens dentro de cem viram dez
/// mil execuções: o fluxo fica lento e costuma estourar o limite de ações.
/// Quase sempre dá para resolver com filtro ou consulta melhor antes do laço.
/// </summary>
[Rule("FL222", RuleCategory.Flow, Severity.Warning)]
public sealed class NestedLoopRule : IRule
{
    private static readonly string[] LoopTypes = ["Foreach", "Until", "Do_Until"];

    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            foreach (var acao in flow.AllActions())
            {
                if (!IsLoop(acao))
                    continue;

                ctx.Evaluated(1);

                var interno = acao.Children
                    .SelectMany(c => c.SelfAndDescendants())
                    .FirstOrDefault(IsLoop);

                if (interno is not null)
                {
                    ctx.Report(
                        interno.Location,
                        $"O laço '{interno.Name}' está dentro de '{acao.Name}'. "
                        + "Laços aninhados multiplicam as execuções e costumam estourar o "
                        + "limite de ações; filtre ou agregue os dados antes do laço externo.");
                }
            }
        }
    }

    private static bool IsLoop(FlowAction action) =>
        LoopTypes.Contains(action.Type, StringComparer.OrdinalIgnoreCase);
}
