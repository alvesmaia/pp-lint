using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF113 — todos os ramos do If fazem exatamente a mesma coisa, então a
/// condição não muda o resultado. Ou ela é inútil, ou um dos ramos está errado;
/// nos dois casos alguém precisa olhar.
/// </summary>
[Rule("PF113", RuleCategory.PowerFx, Severity.Error)]
public sealed class IdenticalBranchesRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            var aninhados = IfChain.NestedElseIfs(root);

            foreach (var call in AstWalker.Calls(root, "If"))
            {
                if (aninhados.Contains(call))
                    continue;

                var chain = IfChain.From(call);

                // Sem o ramo 'senão', o resultado quando nenhuma condição vale é
                // Blank(), diferente dos demais: a condição ainda decide algo.
                if (chain?.Else is null)
                    continue;

                var ramos = chain.Results.ToList();
                if (ramos.Count < 2)
                    continue;

                // Rand(), Now() e parentes devolvem valor diferente a cada chamada:
                // ramos idênticos no texto não produzem o mesmo resultado.
                if (ramos.Any(LogicHelpers.ContainsVolatileCall))
                    continue;

                ctx.Evaluated(1);

                if (ramos.All(r => AstComparer.AreEquivalent(r, ramos[0])))
                {
                    ctx.Report(
                        property.Location,
                        $"Todos os ramos de '{AstComparer.Quote(call)}' são idênticos, então a condição não muda o "
                        + "resultado. Remova o If ou corrija o ramo que deveria ser diferente.");
                }
            }
        }
    }
}

/// <summary>
/// PF114 — a mesma condição testada duas vezes na mesma cadeia de desvios. Se
/// ela era falsa para chegar ao segundo teste, continua falsa: aquele ramo é
/// código morto.
/// </summary>
[Rule("PF114", RuleCategory.PowerFx, Severity.Error)]
public sealed class UnreachableBranchRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            var aninhados = IfChain.NestedElseIfs(root);

            foreach (var call in AstWalker.Calls(root, "If"))
            {
                if (aninhados.Contains(call))
                    continue;

                var condicoes = IfChain.From(call)?.Pairs.Select(p => p.Condition).ToList();

                // Uma condição só não pode se repetir.
                if (condicoes is null || condicoes.Count < 2)
                    continue;

                ctx.Evaluated(1);

                var repetida = FirstRepeatedCondition(condicoes);
                if (repetida is not null)
                {
                    ctx.Report(
                        property.Location,
                        $"A condição '{AstComparer.Quote(repetida)}' é testada de novo depois de já ter falhado. "
                        + "Se ela era falsa para chegar ali, continua falsa — esse ramo nunca executa.");
                }
            }
        }
    }

    /// <summary>
    /// A primeira condição que reaparece na cadeia. Condições voláteis ficam de
    /// fora: 'Rand() > 0.5' testado duas vezes pode dar respostas diferentes, e
    /// o segundo ramo é alcançável de verdade.
    /// </summary>
    private static TexlNode? FirstRepeatedCondition(List<TexlNode> condicoes)
    {
        for (var i = 1; i < condicoes.Count; i++)
        {
            if (LogicHelpers.ContainsVolatileCall(condicoes[i]))
                continue;

            for (var j = 0; j < i; j++)
            {
                if (AstComparer.AreEquivalent(condicoes[j], condicoes[i]))
                    return condicoes[i];
            }
        }

        return null;
    }
}
