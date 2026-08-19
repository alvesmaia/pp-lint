using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF104 — identificador lido que não é variável definida, controle, tela,
/// data source, função, enum nem escopo de linha. Quase sempre é erro de
/// digitação, e no Power Fx isso vira branco em silêncio em vez de erro.
///
/// O denominador é o número de nomes distintos que chegaram a ser candidatos a
/// variável: só assim o índice mede "quantos nomes lidos existem de verdade".
/// </summary>
[Rule("PF104", RuleCategory.PowerFx, Severity.Error)]
public sealed class UndefinedVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            var definidas = graph.Definitions
                .Select(d => d.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var naoDefinidas = graph.UnresolvedReads
                .Select(r => r.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            ctx.Evaluated(definidas.Count + naoDefinidas.Count);

            foreach (var referencia in graph.UnresolvedReads)
            {
                ctx.Report(
                    referencia.Location,
                    $"'{referencia.Name}' é lido mas nunca definido — não é variável, controle, "
                    + "tela, fonte de dados nem função conhecida. Verifique se o nome está correto.");
            }
        }
    }
}
