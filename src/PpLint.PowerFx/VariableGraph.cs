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
    private readonly Dictionary<string, HashSet<string>> _writesByScreen;
    private readonly HashSet<string> _writtenAtAppLevel;
    private readonly List<VariableReference> _unresolved;

    private VariableGraph(
        Dictionary<string, VariableDefinition> definitions,
        Dictionary<string, HashSet<string>> readsByScreen,
        Dictionary<string, HashSet<string>> writesByScreen,
        HashSet<string> writtenAtAppLevel,
        List<VariableReference> unresolved)
    {
        _definitions = definitions;
        _readsByScreen = readsByScreen;
        _writesByScreen = writesByScreen;
        _writtenAtAppLevel = writtenAtAppLevel;
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

    /// <summary>
    /// A variável é escrita fora de qualquer tela — App.OnStart, por exemplo.
    /// Ali UpdateContext não existe, então sugerir contexto seria conselho impossível.
    /// </summary>
    public bool IsWrittenAtAppLevel(string name) => _writtenAtAppLevel.Contains(name);

    /// <summary>
    /// Telas que leem OU escrevem a variável. Quem pergunta "isto cabe numa
    /// variável de contexto?" precisa das duas pontas: Set numa tela e leitura
    /// em outra é justamente o caso em que contexto não serve.
    /// </summary>
    public IReadOnlyList<string> ScreensTouching(string name)
    {
        var telas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_readsByScreen.TryGetValue(name, out var lendo))
            telas.UnionWith(lendo);

        if (_writesByScreen.TryGetValue(name, out var escrevendo))
            telas.UnionWith(escrevendo);

        return telas.ToList();
    }

    /// <summary>
    /// Construir o grafo custa o parse de todas as fórmulas do app, e oito regras
    /// pedem o mesmo grafo na mesma execução — sem cache, um app de 2.279 fórmulas
    /// passava de 1,6 s para 9 s. A tabela é por referência de app e não impede
    /// coleta de lixo; o IR é imutável depois da extração, então o valor não envelhece.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CanvasApp, VariableGraph> Cache = new();

    public static VariableGraph Build(CanvasApp app) =>
        Cache.GetValue(app, BuildUncached);

    private static VariableGraph BuildUncached(CanvasApp app)
    {
        var resolver = SymbolResolver.Build(app);
        var definitions = new Dictionary<string, VariableDefinition>(StringComparer.OrdinalIgnoreCase);

        // As definições são chaveadas por tela quando são de contexto, então
        // perguntas do tipo "este nome existe no app?" precisam deste índice à
        // parte — sem ele, toda variável de contexto vira "nunca definida".
        var definedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readsByScreen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var writesByScreen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var writtenAtAppLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidateReads = new List<(string Name, SourceLocation Location)>();
        var inferredDataSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Duas passadas. A primeira descobre tudo o que o app define, porque uma
        // leitura pode aparecer numa fórmula anterior à definição — e porque o
        // Studio registra as coleções em DataSources.json, o que faria o
        // resolvedor classificá-las como fonte de dados e descartar suas leituras.
        // A árvore é parseada uma vez e reaproveitada: reparsear criaria nós
        // novos, e o conjunto de nós marcados como definição deixaria de casar.
        var analisadas = new List<(PowerFxProperty Property, string? Screen, TexlNode Root, HashSet<TexlNode> Defined)>();

        foreach (var (property, screen) in AllFormulas(app))
        {
            var parsed = PowerFxParser.Parse(property.Script);
            if (parsed.Root is null)
                continue;

            var defined = new HashSet<TexlNode>();

            CollectInferredDataSources(parsed.Root, inferredDataSources);
            CollectDefinitions(parsed.Root, property, screen, resolver, definitions, definedNames, defined);

            analisadas.Add((property, screen, parsed.Root, defined));
        }

        foreach (var (property, screen, root, defined) in analisadas)
        {
            var rowScopes = RowScopeCollector.Collect(root);

            foreach (var identifier in AstWalker.Identifiers(root))
            {
                if (defined.Contains(identifier))
                {
                    // Alvo de um Set: registra a tela que escreve, para que
                    // PF105 saiba que a variável cruza fronteira de tela.
                    var escrito = identifier.Ident.Name.Value;

                    if (screen is null)
                    {
                        writtenAtAppLevel.Add(escrito);
                    }
                    else
                    {
                        if (!writesByScreen.TryGetValue(escrito, out var telasEscrita))
                            writesByScreen[escrito] = telasEscrita = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        telasEscrita.Add(screen);
                    }

                    continue;
                }

                var name = identifier.Ident.Name.Value;

                // Um nome que o app define é variável, mesmo que também apareça
                // nos metadados como fonte de dados.
                if (!definedNames.Contains(name) && !resolver.IsVariableCandidate(name, rowScopes))
                    continue;

                if (!readsByScreen.TryGetValue(name, out var screens))
                    readsByScreen[name] = screens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (screen is not null)
                    screens.Add(screen);

                // Continua contando como leitura — senão PF101 acusaria variáveis
                // usadas dentro de Filter — mas não entra na lista de "nunca
                // definidos": ali dentro o nome pode ser uma coluna do registro,
                // e não temos o schema da tabela para distinguir.
                if (!IsInsideRowScopeFunction(identifier) && !IsQualifierOfDottedName(identifier))
                    candidateReads.Add((name, property.Location));
            }
        }

        var unresolved = candidateReads
            .Where(r => !definedNames.Contains(r.Name))
            .Where(r => !inferredDataSources.Contains(r.Name))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new VariableReference(g.Key, g.First().Location))
            .ToList();

        return new VariableGraph(definitions, readsByScreen, writesByScreen, writtenAtAppLevel, unresolved);
    }

    /// <summary>
    /// Funções que abrem escopo de linha: dentro delas, um identificador solto
    /// costuma ser coluna do registro (Filter(Pedidos, Title = "x")), e não
    /// temos o schema da tabela para provar o contrário.
    /// </summary>
    private static readonly HashSet<string> RowScopeFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Filter", "LookUp", "ForAll", "With", "Sort", "SortByColumns", "Search",
        "AddColumns", "DropColumns", "RenameColumns", "ShowColumns",
        "GroupBy", "Ungroup", "Concat", "Sum", "Average", "Min", "Max", "StdevP", "VarP",
        "CountIf", "CountRows", "First", "FirstN", "Last", "LastN", "Distinct",
        "Patch", "UpdateIf", "RemoveIf", "Collect", "ClearCollect", "Refresh",
    };

    /// <summary>
    /// Um nome no primeiro argumento de Refresh, Patch, LookUp ou Filter é uma
    /// fonte de dados, ainda que não apareça nos metadados do app — nem toda
    /// conexão fica registrada em DataSources.json. Inferir pelo uso evita
    /// acusar de "nome inexistente" algo que o app claramente consulta.
    /// </summary>
    private static void CollectInferredDataSources(TexlNode root, HashSet<string> inferred)
    {
        foreach (var name in RowScopeFunctions)
        {
            foreach (var call in AstWalker.Calls(root, name))
            {
                var args = call.Args?.ChildNodes;
                if (args is { Count: > 0 } && args[0] is FirstNameNode source)
                    inferred.Add(source.Ident.Name.Value);
            }
        }
    }

    /// <summary>
    /// O nome à esquerda de um ponto — TraceSeverity.Warning, Icon.Add,
    /// ImageRotation.None. O Power Apps traz dezenas desses enums e objetos de
    /// host, e listá-los um a um seria perseguir alvo móvel. Um erro de digitação
    /// nessa posição escapa, o que é o lado certo para errar numa regra de
    /// severidade Error.
    /// </summary>
    private static bool IsQualifierOfDottedName(TexlNode node) =>
        node.Parent is DottedNameNode dotted && ReferenceEquals(dotted.Left, node);

    private static bool IsInsideRowScopeFunction(TexlNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is CallNode call
                && AstWalker.FunctionName(call) is { } name
                && RowScopeFunctions.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectDefinitions(
        TexlNode root,
        PowerFxProperty property,
        string? screen,
        SymbolResolver resolver,
        Dictionary<string, VariableDefinition> definitions,
        HashSet<string> definedNames,
        HashSet<TexlNode> defined)
    {
        foreach (var call in AstWalker.Calls(root, "Set"))
            AddFromFirstArgument(call, VariableKind.Global, screen: null, resolver, property, definitions, definedNames, defined);

        foreach (var name in new[] { "Collect", "ClearCollect", "Clear" })
            foreach (var call in AstWalker.Calls(root, name))
                AddFromFirstArgument(call, VariableKind.Collection, screen: null, resolver, property, definitions, definedNames, defined);

        foreach (var call in AstWalker.Calls(root, "UpdateContext"))
            AddFromRecordArgument(call, argumentIndex: 0, screen, property, definitions, definedNames);

        // Navigate(destino, transição, {contexto}) — a variável nasce no destino,
        // desde que o destino seja mesmo uma tela. Em navegação dinâmica
        // (Navigate(varProxima, ...)) o nome não é tela nenhuma, e arquivar a
        // variável ali faria a PF102 nunca encontrar suas leituras.
        foreach (var call in AstWalker.Calls(root, "Navigate"))
        {
            var destino = FirstArgumentName(call);
            var telaDestino = destino is not null
                              && resolver.Resolve(destino, EmptyScopes) == SymbolKind.Screen
                ? destino
                : screen;

            AddFromRecordArgument(call, argumentIndex: 2, telaDestino, property, definitions, definedNames);
        }
    }

    private static readonly IReadOnlySet<string> EmptyScopes = new HashSet<string>();

    private static void AddFromFirstArgument(
        CallNode call,
        VariableKind kind,
        string? screen,
        SymbolResolver resolver,
        PowerFxProperty property,
        Dictionary<string, VariableDefinition> definitions,
        HashSet<string> definedNames,
        HashSet<TexlNode> defined)
    {
        var args = call.Args?.ChildNodes;
        if (args is null || args.Count == 0 || args[0] is not FirstNameNode target)
            return;

        var name = target.Ident.Name.Value;

        // Collect(Ativos, ...) grava numa fonte de dados de verdade; ali não
        // nasce coleção, e cobrar convenção de nome ou desuso seria falso.
        if (kind == VariableKind.Collection
            && resolver.Resolve(name, EmptyScopes) == SymbolKind.DataSource)
        {
            defined.Add(target);
            return;
        }

        defined.Add(target);
        Add(definitions, definedNames, name, kind, screen, property.Location);
    }

    private static void AddFromRecordArgument(
        CallNode call,
        int argumentIndex,
        string? screen,
        PowerFxProperty property,
        Dictionary<string, VariableDefinition> definitions,
        HashSet<string> definedNames)
    {
        var args = call.Args?.ChildNodes;
        if (args is null || args.Count <= argumentIndex || args[argumentIndex] is not RecordNode record)
            return;

        foreach (var id in record.Ids)
            Add(definitions, definedNames, id.Name.Value, VariableKind.Context, screen, property.Location);
    }

    private static string? FirstArgumentName(CallNode call)
    {
        var args = call.Args?.ChildNodes;
        return args is { Count: > 0 } && args[0] is FirstNameNode name ? name.Ident.Name.Value : null;
    }

    /// <summary>
    /// A chave inclui a tela para variáveis de contexto: locX de scrA e locX de
    /// scrB são variáveis diferentes, e guardar só uma faria a PF102 perder o
    /// achado da outra. Globais e coleções são do app inteiro, então usam o nome.
    /// </summary>
    private static void Add(
        Dictionary<string, VariableDefinition> definitions,
        HashSet<string> definedNames,
        string name,
        VariableKind kind,
        string? screen,
        SourceLocation location)
    {
        if (string.IsNullOrEmpty(name))
            return;

        var key = kind == VariableKind.Context ? $"{screen} {name}" : name;

        definedNames.Add(name);

        if (definitions.ContainsKey(key))
            return;

        definitions[key] = new VariableDefinition(name, kind, screen, location);
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
