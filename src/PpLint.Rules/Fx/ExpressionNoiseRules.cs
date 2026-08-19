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
            // Numa cadeia como Not(Not(Not(x))), cada nó interno também nega duas
            // vezes. Reportar todos daria três avisos para um problema só, então
            // ignoro quem é operando de outra negação e falo apenas do topo.
            var aninhados = AstWalker.Descendants(root)
                .Select(NegatedOperand)
                .OfType<TexlNode>()
                .ToHashSet();

            foreach (var node in AstWalker.Descendants(root))
            {
                var interno = NegatedOperand(node);
                if (interno is null || aninhados.Contains(node))
                    continue;

                ctx.Evaluated(1);

                var duplo = NegatedOperand(interno);
                if (duplo is not null)
                {
                    ctx.Report(
                        property.Location,
                        $"'{AstComparer.Quote(node)}' nega duas vezes e equivale a '{AstComparer.Render(duplo)}'. Remova a dupla negação.");
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
                        $"'{AstComparer.Quote(call)}' filtra por 'true' e devolve '{AstComparer.Render(args[0])}' inteiro. "
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

                var outro = IsEmptyText(node.Left) ? node.Right : IsEmptyText(node.Right) ? node.Left : null;

                // Só reclamo quando o outro lado comprovadamente já é texto. O
                // idioma 'varQtd & ""' converte número em texto: mandar remover a
                // concatenação mudaria o tipo do resultado.
                if (outro is not null && IsKnownText(outro))
                {
                    ctx.Report(
                        property.Location,
                        $"'{AstComparer.Quote(node)}' concatena com texto vazio, o que não muda o resultado. "
                        + "Remova a concatenação.");
                }
            }
        }
    }

    private static bool IsEmptyText(TexlNode node) =>
        node is StrLitNode texto && texto.Value.Length == 0;

    /// <summary>
    /// Funções cujo resultado é sempre texto. A lista é curta de propósito:
    /// na dúvida, calo a boca — esta regra é Info e um falso positivo aqui
    /// manda quebrar uma fórmula que funciona.
    /// </summary>
    private static readonly string[] TextFunctions =
    [
        "Text", "Concatenate", "Concat", "Left", "Right", "Mid", "Trim", "TrimEnds",
        "Upper", "Lower", "Proper", "Substitute", "Replace", "JSON", "EncodeUrl",
    ];

    private static bool IsKnownText(TexlNode node) => node switch
    {
        StrLitNode => true,
        StrInterpNode => true,
        BinaryOpNode { Op: BinaryOp.Concat } => true,
        CallNode call => AstWalker.FunctionName(call) is { } nome
            && TextFunctions.Contains(nome, StringComparer.OrdinalIgnoreCase),
        _ => false,
    };
}
