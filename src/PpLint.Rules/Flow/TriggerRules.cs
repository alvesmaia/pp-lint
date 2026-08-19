using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL240 — fluxo sem descrição. Quem abre a lista do Power Automate meses depois
/// só tem o nome para entender o que a automação faz.
/// </summary>
[Rule("FL240", RuleCategory.Flow, Severity.Info)]
public sealed class FlowWithoutDescriptionRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            ctx.Evaluated(1);

            if (string.IsNullOrWhiteSpace(flow.Description))
            {
                ctx.Report(
                    flow.Location,
                    $"O fluxo '{flow.Name}' não tem descrição. Explique em uma linha o que ele faz "
                    + "e quando dispara.");
            }
        }
    }
}
