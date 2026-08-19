using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF106 — a mesma variável recebe o mesmo valor literal duas vezes em sequência,
/// sem nada entre as duas atribuições que mude o valor. O segundo Set não faz nada.
///
/// A regra examina apenas cadeias sequenciais (o operador ';'). Dois Set em ramos
/// diferentes de um If são mutuamente exclusivos — só um executa — e tratá-los
/// como sequência produziria acusação falsa em código correto.
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

                foreach (var chain in SequentialChains(parsed.Root))
                    InspectChain(ctx, chain, property);
            }
        }
    }

    /// <summary>
    /// Cada sequência de expressões encadeadas por ';'. Um VariadicOpNode é uma
    /// dessas cadeias; os ramos de um If aparecem como cadeias separadas, porque
    /// entre eles não há ordem de execução — há escolha.
    /// </summary>
    private static IEnumerable<IReadOnlyList<TexlNode>> SequentialChains(TexlNode root)
    {
        foreach (var node in AstWalker.Descendants(root).OfType<VariadicOpNode>())
            yield return node.ChildNodes;

        // Uma fórmula com um único Set não passa por VariadicOpNode.
        if (root is not VariadicOpNode)
            yield return [root];
    }

    private static void InspectChain(LintContext ctx, IReadOnlyList<TexlNode> chain, PowerFxProperty property)
    {
        var lastLiteral = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in chain)
        {
            if (step is not CallNode call
                || !string.Equals(AstWalker.FunctionName(call), "Set", StringComparison.OrdinalIgnoreCase))
                continue;

            var args = call.Args?.ChildNodes;
            if (args is null || args.Count < 2 || args[0] is not FirstNameNode target)
                continue;

            var name = target.Ident.Name.Value;
            var literal = LiteralText(args[1]);

            if (literal is null)
            {
                // Valor que pode variar entre execuções: o que a variável tinha
                // antes deixa de valer, senão um contador que zera, incrementa e
                // zera de novo seria acusado de redundante.
                lastLiteral.Remove(name);
                continue;
            }

            // Cada Set com valor literal é um alvo examinado. Contar só os
            // redundantes faria Evaluated == Violations, e a regra marcaria 0%
            // de conformidade sempre que achasse algo.
            ctx.Evaluated(1);

            if (lastLiteral.TryGetValue(name, out var anterior) && anterior == literal)
            {
                if (reported.Add(name))
                {
                    ctx.Report(
                        property.Location,
                        $"A variável '{name}' recebe o mesmo valor ({literal}) duas vezes em sequência "
                        + "nesta fórmula. O segundo Set não tem efeito.");
                }
            }

            lastLiteral[name] = literal;
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
