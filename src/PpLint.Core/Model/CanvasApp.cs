namespace PpLint.Core.Model;

public sealed class CanvasApp
{
    public required string Name { get; init; }

    public required SourceLocation Location { get; init; }

    public List<Control> Screens { get; } = [];

    public List<DataSource> DataSources { get; } = [];

    /// <summary>Expressões de nível de app, como App.OnStart.</summary>
    public List<PowerFxProperty> AppProperties { get; } = [];

    /// <summary>
    /// As conexões que o app declara. Diferente de DataSources: uma conexão é
    /// a autorização ao conector, e pode existir sem nenhuma fonte pendurada
    /// nela — que é justamente o caso que a DUP306 procura.
    /// </summary>
    public List<AppConnection> Connections { get; } = [];

    /// <summary>As definições de componente do app, com as propriedades que alguém criou.</summary>
    public List<CanvasComponent> Components { get; } = [];

    public IEnumerable<Control> AllControls() =>
        Screens.SelectMany(s => s.SelfAndDescendants());
}

/// <summary>
/// Uma conexão declarada pelo app.
///
/// <paramref name="DataSourceCount"/> e <paramref name="DependentCount"/> são o
/// que distingue conexão em uso de conexão esquecida: o Studio registra em
/// cada conexão quais fontes vêm dela e quais controles dependem dela.
/// </summary>
public sealed record AppConnection(
    string Id,
    string DisplayName,
    string ConnectorId,
    int DataSourceCount,
    int DependentCount);

/// <summary>
/// Uma propriedade que alguém acrescentou a um componente. As embutidas —
/// Fill, Width, OnSelect — não entram: elas vêm do template e ninguém as
/// escolheu.
/// </summary>
public sealed record ComponentProperty(string Name, string DisplayName, string Type);

public sealed class CanvasComponent
{
    public required string Name { get; init; }

    public required SourceLocation Location { get; init; }

    public List<ComponentProperty> CustomProperties { get; } = [];
}
