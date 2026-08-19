using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF115 — Not(Not(x)) é x. Aparece quando alguém inverte uma condição duas
/// vezes durante uma refatoração e não simplifica no fim.
/// </summary>
[Rule("PF115", RuleCategory.PowerFx, Severity.Warning)]
public sealed class DoubleNegationRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var node in AstWalker.Descendants(root))
            {
                var interno = NegatedOperand(node);
                if (interno is null)
                    continue;

                ctx.Evaluated(1);

                var duplo = NegatedOperand(interno);
                if (duplo is not null)
                {
                    ctx.Report(
                        property.Location,
                        $"'{node}' nega duas vezes e equivale a '{duplo}'. Remova a dupla negação.");
                }
            }
        }
    }

    /// <summary>O operando de uma negação, seja ela Not(x) ou !x; null se o nó não nega.</summary>
    private static TexlNode? NegatedOperand(TexlNode node)
    {
        if (node is UnaryOpNode unary && unary.Op == UnaryOp.Not)
            return unary.Child;

        if (node is CallNode call
            && string.Equals(AstWalker.FunctionName(call), "Not", StringComparison.OrdinalIgnoreCase))
        {
            var args = call.Args?.ChildNodes;
            if (args is { Count: 1 })
                return args[0];
        }

        return null;
    }
}

/// <summary>
/// PF116 — Filter(fonte, true) devolve a fonte inteira. O filtro engana quem lê
/// a fórmula e, sobre fonte delegável, ainda atrapalha a delegação.
/// </summary>
[Rule("PF116", RuleCategory.PowerFx, Severity.Warning)]
public sealed class PointlessFilterRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var call in AstWalker.Calls(root, "Filter"))
            {
                var args = call.Args?.ChildNodes;
                if (args is null || args.Count < 2)
                    continue;

                ctx.Evaluated(1);

                // Só 'true' é filtro sem efeito. 'false' devolve vazio — outro
                // problema, e sugerir remover o filtro ali seria errado.
                var todosVerdadeiros = args
                    .Skip(1)
                    .All(a => LogicHelpers.IsBooleanLiteral(a, out var valor) && valor);

                if (todosVerdadeiros)
                {
                    ctx.Report(
                        property.Location,
                        $"'{call}' filtra por 'true' e devolve '{args[0]}' inteiro. "
                        + "Remova o Filter ou escreva a condição real.");
                }
            }
        }
    }
}

/// <summary>
/// PF118 — concatenar com texto vazio não muda nada. Costuma sobrar de uma
/// refatoração; é inofensivo, por isso Info.
/// </summary>
[Rule("PF118", RuleCategory.PowerFx, Severity.Info)]
public sealed class EmptyConcatenationRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var node in AstWalker.Descendants(root).OfType<BinaryOpNode>())
            {
                if (node.Op != BinaryOp.Concat)
                    continue;

                ctx.Evaluated(1);

                if (IsEmptyText(node.Left) || IsEmptyText(node.Right))
                {
                    ctx.Report(
                        property.Location,
                        $"'{node}' concatena com texto vazio, o que não muda o resultado. "
                        + "Remova a concatenação.");
                }
            }
        }
    }

    private static bool IsEmptyText(TexlNode node) =>
        node is StrLitNode texto && texto.Value.Length == 0;
}
