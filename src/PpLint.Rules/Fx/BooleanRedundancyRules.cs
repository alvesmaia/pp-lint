using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF111 — If(cond, true, false) é apenas cond, e If(cond, false, true) é
/// Not(cond). A forma longa esconde a intenção atrás de um desvio que não existe.
/// </summary>
[Rule("PF111", RuleCategory.PowerFx, Severity.Warning)]
public sealed class RedundantBooleanIfRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var call in AstWalker.Calls(root, "If"))
            {
                var args = call.Args?.ChildNodes;
                if (args is null || args.Count != 3)
                    continue;

                ctx.Evaluated(1);

                if (!LogicHelpers.IsBooleanLiteral(args[1], out var entao)
                    || !LogicHelpers.IsBooleanLiteral(args[2], out var senao)
                    || entao == senao)
                {
                    continue;
                }

                var condicao = AstComparer.Render(args[0]);
                var sugestao = entao ? condicao : $"Not({condicao})";

                ctx.Report(
                    property.Location,
                    $"'{AstComparer.Quote(call)}' pode ser escrito como '{sugestao}'. "
                    + "O If não acrescenta nada quando os dois ramos são booleanos opostos.");
            }
        }
    }
}

/// <summary>
/// PF112 — comparar um valor booleano com true ou false não acrescenta
/// informação: 'x = true' é 'x', e 'x = false' é 'Not(x)'.
/// Comparação entre dois literais é condição constante e cabe à PF110.
/// </summary>
[Rule("PF112", RuleCategory.PowerFx, Severity.Warning)]
public sealed class BooleanComparisonRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var node in AstWalker.Descendants(root).OfType<BinaryOpNode>())
            {
                if (node.Op is not (BinaryOp.Equal or BinaryOp.NotEqual))
                    continue;

                // Toda comparação de igualdade é alvo desta regra, inclusive as que
                // passam. Contar só as reportadas faria Evaluated == Violations, e a
                // conformidade da PF112 seria sempre 0% quando ela achasse algo.
                ctx.Evaluated(1);

                var literalEsquerda = LogicHelpers.IsBooleanLiteral(node.Left, out var valorEsquerda);
                var literalDireita = LogicHelpers.IsBooleanLiteral(node.Right, out var valorDireita);

                // Os dois lados literais: condição constante, assunto da PF110.
                if (literalEsquerda && literalDireita)
                    continue;

                if (!literalEsquerda && !literalDireita)
                    continue;

                var expressao = literalEsquerda ? node.Right : node.Left;
                var literal = literalEsquerda ? valorEsquerda : valorDireita;

                // '<> true' equivale a 'Not(x)'; '= false' também.
                var afirmativo = node.Op == BinaryOp.Equal ? literal : !literal;
                var render = AstComparer.Render(expressao);
                var sugestao = afirmativo ? render : $"Not({render})";

                ctx.Report(
                    property.Location,
                    $"'{AstComparer.Quote(node)}' pode ser escrito como '{sugestao}'. "
                    + "Comparar um booleano com true ou false não acrescenta informação.");
            }
        }
    }
}
