using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx;

public enum VariableKind { Global, Context, Collection }

/// <summary>Uma variável e onde ela nasceu. Screen só vale para Context.</summary>
public sealed record VariableDefinition(
    string Name,
    VariableKind Kind,
    string? Screen,
    SourceLocation Location);

public sealed record VariableReference(string Name, SourceLocation Location);

/// <summary>
/// Variáveis de um canvas app, com tipo e escopo. Globais e coleções valem no
/// app inteiro; variáveis de contexto pertencem a uma tela — e uma definida via
/// Navigate nasce na tela de destino, não na de origem.
/// </summary>
public sealed class VariableGraph
{
    private readonly Dictionary<string, VariableDefinition> _definitions;
    private readonly Dictionary<string, HashSet<string>> _readsByScreen;
    private readonly List<VariableReference> _unresolved;

    private VariableGraph(
        Dictionary<string, VariableDefinition> definitions,
        Dictionary<string, HashSet<string>> readsByScreen,
        List<VariableReference> unresolved)
    {
        _definitions = definitions;
        _readsByScreen = readsByScreen;
        _unresolved = unresolved;
    }

    public IReadOnlyList<VariableDefinition> Definitions => _definitions.Values.ToList();

    /// <summary>Mantida para a PF101, que já está em produção.</summary>
    public IReadOnlyList<VariableDefinition> Globals =>
        _definitions.Values.Where(d => d.Kind == VariableKind.Global).ToList();

    public IReadOnlyList<VariableReference> UnresolvedReads => _unresolved;

    public bool IsRead(string name) => _readsByScreen.ContainsKey(name);

    public bool IsReadInScreen(string name, string screen) =>
        _readsByScreen.TryGetValue(name, out var screens) && screens.Contains(screen);

    public IReadOnlyList<string> ScreensReading(string name) =>
        _readsByScreen.TryGetValue(name, out var screens) ? screens.ToList() : [];

    public static VariableGraph Build(CanvasApp app)
    {
        var resolver = SymbolResolver.Build(app);
        var definitions = new Dictionary<string, VariableDefinition>(StringComparer.OrdinalIgnoreCase);
        var readsByScreen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var candidateReads = new List<(string Name, SourceLocation Location)>();

        foreach (var (property, screen) in AllFormulas(app))
        {
            var parsed = PowerFxParser.Parse(property.Script);
            if (parsed.Root is null)
                continue;

            var rowScopes = RowScopeCollector.Collect(parsed.Root);
            var defined = new HashSet<TexlNode>();

            CollectDefinitions(parsed.Root, property, screen, definitions, defined);

            foreach (var identifier in AstWalker.Identifiers(parsed.Root))
            {
                if (defined.Contains(identifier))
                    continue;

                var name = identifier.Ident.Name.Value;

                if (!resolver.IsVariableCandidate(name, rowScopes))
                    continue;

                if (!readsByScreen.TryGetValue(name, out var screens))
                    readsByScreen[name] = screens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (screen is not null)
                    screens.Add(screen);

                candidateReads.Add((name, property.Location));
            }
        }

        var unresolved = candidateReads
            .Where(r => !definitions.ContainsKey(r.Name))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new VariableReference(g.Key, g.First().Location))
            .ToList();

        return new VariableGraph(definitions, readsByScreen, unresolved);
    }

    private static void CollectDefinitions(
        TexlNode root,
        PowerFxProperty property,
        string? screen,
        Dictionary<string, VariableDefinition> definitions,
        HashSet<TexlNode> defined)
    {
        foreach (var call in AstWalker.Calls(root, "Set"))
            AddFromFirstArgument(call, VariableKind.Global, screen: null, property, definitions, defined);

        foreach (var name in new[] { "Collect", "ClearCollect", "Clear" })
            foreach (var call in AstWalker.Calls(root, name))
                AddFromFirstArgument(call, VariableKind.Collection, screen: null, property, definitions, defined);

        foreach (var call in AstWalker.Calls(root, "UpdateContext"))
            AddFromRecordArgument(call, argumentIndex: 0, screen, property, definitions);

        // Navigate(destino, transição, {contexto}) — a variável nasce no destino.
        foreach (var call in AstWalker.Calls(root, "Navigate"))
        {
            var destino = FirstArgumentName(call);
            AddFromRecordArgument(call, argumentIndex: 2, destino ?? screen, property, definitions);
        }
    }

    private static void AddFromFirstArgument(
        CallNode call,
        VariableKind kind,
        string? screen,
        PowerFxProperty property,
        Dictionary<string, VariableDefinition> definitions,
        HashSet<TexlNode> defined)
    {
        var args = call.Args?.ChildNodes;
        if (args is null || args.Count == 0 || args[0] is not FirstNameNode target)
            return;

        defined.Add(target);
        Add(definitions, target.Ident.Name.Value, kind, screen, property.Location);
    }

    private static void AddFromRecordArgument(
        CallNode call,
        int argumentIndex,
        string? screen,
        PowerFxProperty property,
        Dictionary<string, VariableDefinition> definitions)
    {
        var args = call.Args?.ChildNodes;
        if (args is null || args.Count <= argumentIndex || args[argumentIndex] is not RecordNode record)
            return;

        foreach (var id in record.Ids)
            Add(definitions, id.Name.Value, VariableKind.Context, screen, property.Location);
    }

    private static string? FirstArgumentName(CallNode call)
    {
        var args = call.Args?.ChildNodes;
        return args is { Count: > 0 } && args[0] is FirstNameNode name ? name.Ident.Name.Value : null;
    }

    private static void Add(
        Dictionary<string, VariableDefinition> definitions,
        string name,
        VariableKind kind,
        string? screen,
        SourceLocation location)
    {
        if (string.IsNullOrEmpty(name) || definitions.ContainsKey(name))
            return;

        definitions[name] = new VariableDefinition(name, kind, screen, location);
    }

    /// <summary>Cada fórmula do app com a tela a que pertence (null para App.OnStart).</summary>
    private static IEnumerable<(PowerFxProperty Property, string? Screen)> AllFormulas(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return (property, null);

        foreach (var screen in app.Screens)
            foreach (var control in screen.SelfAndDescendants())
                foreach (var property in control.Properties)
                    yield return (property, screen.Name);
    }
}
