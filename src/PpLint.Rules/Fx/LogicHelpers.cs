using Microsoft.PowerFx.Syntax;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// O que as regras de lógica repetem: percorrer as fórmulas já parseadas e
/// reconhecer literais booleanos.
/// </summary>
internal static class LogicHelpers
{
    public static IEnumerable<(PowerFxProperty Property, TexlNode Root)> ParsedProperties(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var property in AllProperties(app))
            {
                var parsed = PowerFxParser.Parse(property.Script);
                if (parsed.Root is not null)
                    yield return (property, parsed.Root);
            }
        }
    }

    public static bool IsBooleanLiteral(TexlNode node, out bool value)
    {
        if (node is BoolLitNode boolean)
        {
            value = boolean.Value;
            return true;
        }

        value = false;
        return false;
    }

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}
