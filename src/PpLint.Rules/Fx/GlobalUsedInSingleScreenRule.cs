using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF105 — variável global lida em uma única tela. Não quebra nada, mas amplia
/// o alcance de um estado sem necessidade: uma variável de contexto expressaria
/// melhor a intenção e morreria junto com a tela.
/// Global nunca lida é assunto da PF101 e não é avaliada aqui.
/// </summary>
[Rule("PF105", RuleCategory.PowerFx, Severity.Info)]
public sealed class GlobalUsedInSingleScreenRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Definitions.Where(d => d.Kind == VariableKind.Global))
            {
                var screens = graph.ScreensReading(variable.Name);
                if (screens.Count == 0)
                    continue;

                ctx.Evaluated(1);

                if (screens.Count == 1)
                {
                    ctx.Report(
                        variable.Location,
                        $"A variável global '{variable.Name}' só é lida na tela '{screens[0]}'. "
                        + "Uma variável de contexto (UpdateContext) limitaria o estado a essa tela.");
                }
            }
        }
    }
}
