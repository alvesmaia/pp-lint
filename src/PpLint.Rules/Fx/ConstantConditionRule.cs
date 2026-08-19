using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF110 — condição cujo resultado não depende de nada em tempo de execução,
/// como If(2 &gt; 1, ...) ou If(true, ...). Ou é resto de depuração, ou é um
/// ramo que nunca executa; nos dois casos o comportamento real diverge do
/// que o código aparenta.
/// </summary>
[Rule("PF110", RuleCategory.PowerFx, Severity.Error)]
public sealed class ConstantConditionRule : IRule
{
    private static readonly BinaryOp[] ComparisonOperators =
    [
        BinaryOp.Equal,
        BinaryOp.NotEqual,
        BinaryOp.Less,
        BinaryOp.LessEqual,
        BinaryOp.Greater,
        BinaryOp.GreaterEqual,
    ];

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var property in AllProperties(app))
            {
                var parsed = PowerFxParser.Parse(property.Script);
                if (parsed.Root is null)
                    continue;

                foreach (var condition in Conditions(parsed.Root))
                {
                    ctx.Evaluated(1);

                    if (IsConstant(condition))
                    {
                        ctx.Report(
                            property.Location,
                            $"A condição '{condition}' tem resultado constante e não depende de nada "
                            + "em tempo de execução. Remova a condição ou use um valor real.");
                    }
                }
            }
        }
    }

    /// <summary>Condições examinadas: o primeiro argumento de cada If e toda comparação.</summary>
    private static IEnumerable<TexlNode> Conditions(TexlNode root)
    {
        var seen = new HashSet<TexlNode>();

        foreach (var call in AstWalker.Calls(root, "If"))
        {
            var args = call.Args?.ChildNodes;
            if (args is { Count: > 0 } && seen.Add(args[0]))
                yield return args[0];
        }

        foreach (var node in AstWalker.Descendants(root).OfType<BinaryOpNode>())
        {
            if (ComparisonOperators.Contains(node.Op) && seen.Add(node))
                yield return node;
        }
    }

    private static bool IsConstant(TexlNode condition) => condition switch
    {
        BoolLitNode => true,
        BinaryOpNode binary => ComparisonOperators.Contains(binary.Op)
                               && IsLiteral(binary.Left)
                               && IsLiteral(binary.Right),
        _ => false,
    };

    private static bool IsLiteral(TexlNode node) =>
        node is NumLitNode or DecLitNode or BoolLitNode or StrLitNode;

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}
