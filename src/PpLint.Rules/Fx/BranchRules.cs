using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF113 — os dois ramos do If fazem exatamente a mesma coisa, então a
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
            foreach (var call in AstWalker.Calls(root, "If"))
            {
                var args = call.Args?.ChildNodes;
                if (args is null || args.Count != 3)
                    continue;

                ctx.Evaluated(1);

                if (AstComparer.AreEquivalent(args[1], args[2]))
                {
                    ctx.Report(
                        property.Location,
                        $"Os dois ramos de '{call}' são idênticos, então a condição não muda o "
                        + "resultado. Remova o If ou corrija o ramo que deveria ser diferente.");
                }
            }
        }
    }
}

/// <summary>
/// PF114 — a mesma condição testada de novo dentro do próprio ramo 'senão'.
/// Se ela era falsa para chegar ali, continua falsa: o ramo interno é código morto.
/// </summary>
[Rule("PF114", RuleCategory.PowerFx, Severity.Error)]
public sealed class UnreachableBranchRule : IRule
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

                // O ramo 'senão' precisa ser outro If para haver o que analisar.
                if (args[2] is not CallNode interno
                    || !string.Equals(AstWalker.FunctionName(interno), "If", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var argsInternos = interno.Args?.ChildNodes;
                if (argsInternos is null || argsInternos.Count < 2)
                    continue;

                ctx.Evaluated(1);

                if (AstComparer.AreEquivalent(args[0], argsInternos[0]))
                {
                    ctx.Report(
                        property.Location,
                        $"A condição '{args[0]}' é testada de novo dentro do próprio ramo 'senão'. "
                        + "Se ela era falsa para chegar ali, continua falsa — esse ramo nunca executa.");
                }
            }
        }
    }
}
