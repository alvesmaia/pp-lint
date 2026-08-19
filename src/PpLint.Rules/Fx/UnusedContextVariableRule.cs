using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF102 — variável de contexto criada e nunca lida na tela onde vive.
/// A checagem é por tela porque é assim que o Power Fx funciona: o mesmo nome
/// em outra tela é outra variável, e ler lá não mantém esta viva.
/// </summary>
[Rule("PF102", RuleCategory.PowerFx, Severity.Warning)]
public sealed class UnusedContextVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Definitions.Where(d => d.Kind == VariableKind.Context))
            {
                ctx.Evaluated(1);

                if (variable.Screen is null || !graph.IsReadInScreen(variable.Name, variable.Screen))
                {
                    ctx.Report(
                        variable.Location,
                        $"A variável de contexto '{variable.Name}' é definida e nunca lida na tela "
                        + $"'{variable.Screen ?? "(desconhecida)"}'. Remova a definição ou use o valor.");
                }
            }
        }
    }
}
