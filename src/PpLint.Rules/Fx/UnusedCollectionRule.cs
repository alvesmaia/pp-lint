using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF103 — coleção criada e nunca usada. Coleções são do app inteiro, então
/// basta ser lida em qualquer tela. Uma coleção órfã ocupa memória do cliente
/// e costuma sobrar de uma tela que mudou de ideia.
/// </summary>
[Rule("PF103", RuleCategory.PowerFx, Severity.Warning)]
public sealed class UnusedCollectionRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Definitions.Where(d => d.Kind == VariableKind.Collection))
            {
                ctx.Evaluated(1);

                if (!graph.IsRead(variable.Name))
                {
                    ctx.Report(
                        variable.Location,
                        $"A coleção '{variable.Name}' é criada e nunca usada. "
                        + "Remova o Collect/ClearCollect ou passe a consumir os dados.");
                }
            }
        }
    }
}
