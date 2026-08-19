using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF101 — variável global definida por Set() e nunca lida em nenhuma
/// expressão do app. Costuma ser resquício de refatoração e mantém em
/// memória estado que ninguém consome.
/// </summary>
[Rule("PF101", RuleCategory.PowerFx, Severity.Warning)]
public sealed class UnusedGlobalVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Globals)
            {
                ctx.Evaluated(1);

                if (!graph.IsRead(variable.Name))
                {
                    ctx.Report(
                        variable.Location,
                        $"A variável global '{variable.Name}' é definida e nunca lida. "
                        + "Remova o Set() ou passe a usar o valor.");
                }
            }
        }
    }
}
