using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx;

public sealed record GlobalVariableDefinition(string Name, SourceLocation Location);

/// <summary>
/// Definições e leituras de variáveis globais de um canvas app.
/// O primeiro argumento de Set() é definição; qualquer outra ocorrência
/// do identificador, em qualquer expressão do app, é leitura.
/// </summary>
public sealed class VariableGraph
{
    private readonly Dictionary<string, GlobalVariableDefinition> _globals;
    private readonly HashSet<string> _reads;

    private VariableGraph(
        Dictionary<string, GlobalVariableDefinition> globals,
        HashSet<string> reads)
    {
        _globals = globals;
        _reads = reads;
    }

    public IReadOnlyList<GlobalVariableDefinition> Globals => _globals.Values.ToList();

    public bool IsRead(string name) => _reads.Contains(name);

    public static VariableGraph Build(CanvasApp app)
    {
        var globals = new Dictionary<string, GlobalVariableDefinition>(StringComparer.OrdinalIgnoreCase);
        var reads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in AllProperties(app))
        {
            var parsed = PowerFxParser.Parse(property.Script);
            if (parsed.Root is null)
                continue;

            var setTargets = new HashSet<TexlNode>();

            foreach (var call in AstWalker.Calls(parsed.Root, "Set"))
            {
                var target = FirstArgumentIdentifier(call);
                if (target is null)
                    continue;

                setTargets.Add(target);

                var name = target.Ident.Name.Value;
                if (!globals.ContainsKey(name))
                    globals[name] = new GlobalVariableDefinition(name, property.Location);
            }

            foreach (var identifier in AstWalker.Identifiers(parsed.Root))
            {
                if (!setTargets.Contains(identifier))
                    reads.Add(identifier.Ident.Name.Value);
            }
        }

        return new VariableGraph(globals, reads);
    }

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }

    /// <summary>O primeiro argumento da chamada, quando é um identificador simples.</summary>
    private static FirstNameNode? FirstArgumentIdentifier(CallNode call)
    {
        var args = call.Args?.ChildNodes;
        if (args is null || args.Count == 0)
            return null;

        return args[0] as FirstNameNode;
    }
}
