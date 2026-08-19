using PpLint.Core.Model;

namespace PpLint.PowerFx;

/// <summary>
/// Responde "este identificador é o quê?" para um app. Sem essa distinção,
/// uma regra que procura variáveis não definidas acusaria todo controle,
/// data source e função que a fórmula referencia.
/// </summary>
public sealed class SymbolResolver
{
    private readonly IReadOnlySet<string> _controls;
    private readonly IReadOnlySet<string> _screens;
    private readonly IReadOnlySet<string> _dataSources;

    private SymbolResolver(
        IReadOnlySet<string> controls,
        IReadOnlySet<string> screens,
        IReadOnlySet<string> dataSources)
    {
        _controls = controls;
        _screens = screens;
        _dataSources = dataSources;
    }

    public static SymbolResolver Build(CanvasApp app)
    {
        var controls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var screens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var control in app.AllControls())
        {
            if (control.IsScreen)
                screens.Add(control.Name);
            else
                controls.Add(control.Name);
        }

        var dataSources = app.DataSources
            .Select(d => d.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new SymbolResolver(controls, screens, dataSources);
    }

    /// <summary>
    /// A ordem importa: um `As` sombreia qualquer outro nome dentro da
    /// expressão onde aparece, então escopo de linha é testado primeiro.
    /// </summary>
    public SymbolKind Resolve(string name, IReadOnlySet<string> rowScopes)
    {
        if (rowScopes.Contains(name))
            return SymbolKind.RowScope;

        if (_screens.Contains(name))
            return SymbolKind.Screen;

        if (_controls.Contains(name))
            return SymbolKind.Control;

        if (_dataSources.Contains(name))
            return SymbolKind.DataSource;

        if (PowerFxBuiltins.FunctionNames.Contains(name))
            return SymbolKind.Function;

        if (PowerFxBuiltins.EnumNames.Contains(name))
            return SymbolKind.Enum;

        return SymbolKind.Unknown;
    }

    public bool IsVariableCandidate(string name, IReadOnlySet<string> rowScopes) =>
        Resolve(name, rowScopes) == SymbolKind.Unknown;
}
