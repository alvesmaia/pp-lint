using Microsoft.PowerFx.Syntax;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// O que as regras de lógica repetem: percorrer as fórmulas já parseadas,
/// reconhecer literais e identificar expressões que mudam de valor a cada
/// execução.
/// </summary>
internal static class LogicHelpers
{
    /// <summary>
    /// Funções que devolvem valor diferente a cada chamada. Duas chamadas iguais
    /// no texto não produzem o mesmo resultado, então comparar as expressões
    /// estruturalmente levaria a conclusões falsas — If(c, Rand(), Rand()) não
    /// tem ramos idênticos.
    /// </summary>
    private static readonly string[] VolatileFunctions =
    [
        "Rand", "RandBetween", "Now", "UTCNow", "Today", "UTCToday",
        "GUID", "Shuffle", "Weekday", "Time", "TimeValue",
    ];

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

    /// <summary>A expressão contém alguma chamada cujo valor muda a cada execução?</summary>
    public static bool ContainsVolatileCall(TexlNode node) =>
        AstWalker.Descendants(node)
            .OfType<CallNode>()
            .Any(c => AstWalker.FunctionName(c) is { } nome
                      && VolatileFunctions.Contains(nome, StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}
