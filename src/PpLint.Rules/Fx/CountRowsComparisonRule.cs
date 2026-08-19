using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF117 — CountRows(x) > 0 percorre a coleção inteira só para saber se ela tem
/// algum item; !IsEmpty(x) para no primeiro. Sobre fonte grande a diferença
/// aparece na tela do usuário.
/// </summary>
[Rule("PF117", RuleCategory.PowerFx, Severity.Warning)]
public sealed class CountRowsComparisonRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var node in AstWalker.Descendants(root).OfType<BinaryOpNode>())
            {
                var contagem = CountRowsArgument(node.Left) ?? CountRowsArgument(node.Right);
                if (contagem is null)
                    continue;

                // Só comparações são alvo. CountRows(x) + 1 usa a contagem como
                // número, não como teste de existência, e não pode entrar no
                // denominador da conformidade.
                if (node.Op is not (BinaryOp.Greater or BinaryOp.GreaterEqual
                    or BinaryOp.Less or BinaryOp.LessEqual
                    or BinaryOp.Equal or BinaryOp.NotEqual))
                {
                    continue;
                }

                ctx.Evaluated(1);

                if (TestaExistencia(node))
                {
                    ctx.Report(
                        property.Location,
                        $"'{AstComparer.Quote(node)}' conta todos os itens só para saber se existe algum. "
                        + $"Use '!IsEmpty({AstComparer.Render(contagem)})', que para no primeiro item.");
                }
            }
        }
    }

    /// <summary>
    /// As formas que significam "tem pelo menos um": contagem maior que zero,
    /// contagem a partir de um, e as mesmas com os lados trocados.
    /// </summary>
    private static bool TestaExistencia(BinaryOpNode node)
    {
        var contagemNaEsquerda = CountRowsArgument(node.Left) is not null;
        var outro = contagemNaEsquerda ? node.Right : node.Left;

        var valor = NumericValue(outro);
        if (valor is null)
            return false;

        return contagemNaEsquerda
            ? (node.Op == BinaryOp.Greater && valor == 0) || (node.Op == BinaryOp.GreaterEqual && valor == 1)
            : (node.Op == BinaryOp.Less && valor == 0) || (node.Op == BinaryOp.LessEqual && valor == 1);
    }

    /// <summary>
    /// O valor de um literal numérico. O parser usa DecLitNode para números
    /// escritos na fórmula e NumLitNode em outras situações; aceitar só um
    /// deles faria a regra nunca disparar.
    /// </summary>
    private static decimal? NumericValue(TexlNode node) => node switch
    {
        DecLitNode dec => dec.ActualDecValue,
        NumLitNode num => (decimal)num.ActualNumValue,
        _ => null,
    };

    /// <summary>O argumento de CountRows(...), ou null se o nó não for essa chamada.</summary>
    private static TexlNode? CountRowsArgument(TexlNode node)
    {
        if (node is not CallNode call
            || !string.Equals(AstWalker.FunctionName(call), "CountRows", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var args = call.Args?.ChildNodes;
        return args is { Count: 1 } ? args[0] : null;
    }
}
