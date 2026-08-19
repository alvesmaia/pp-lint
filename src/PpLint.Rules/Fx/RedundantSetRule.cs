using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF106 — a mesma variável recebe o mesmo valor literal duas vezes na mesma
/// fórmula. O segundo Set não faz nada.
/// Só olha dentro de uma fórmula: entre fórmulas diferentes não há ordem de
/// execução conhecida, e afirmar redundância ali seria chute.
/// </summary>
[Rule("PF106", RuleCategory.PowerFx, Severity.Warning)]
public sealed class RedundantSetRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var property in AllProperties(app))
            {
                var parsed = PowerFxParser.Parse(property.Script);
                if (parsed.Root is null)
                    continue;

                var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var call in AstWalker.Calls(parsed.Root, "Set"))
                {
                    var args = call.Args?.ChildNodes;
                    if (args is null || args.Count < 2 || args[0] is not FirstNameNode target)
                        continue;

                    var literal = LiteralText(args[1]);
                    if (literal is null)
                        continue;

                    var name = target.Ident.Name.Value;

                    if (seen.TryGetValue(name, out var anterior) && anterior == literal)
                    {
                        ctx.Evaluated(1);

                        if (reported.Add(name))
                        {
                            ctx.Report(
                                property.Location,
                                $"A variável '{name}' recebe o mesmo valor ({literal}) duas vezes nesta fórmula. "
                                + "O segundo Set não tem efeito.");
                        }
                    }
                    else
                    {
                        seen[name] = literal;
                    }
                }
            }
        }
    }

    /// <summary>Texto do valor quando ele é literal; null quando pode variar entre execuções.</summary>
    private static string? LiteralText(TexlNode node) => node switch
    {
        NumLitNode or DecLitNode or StrLitNode or BoolLitNode => node.ToString(),
        _ => null,
    };

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}
