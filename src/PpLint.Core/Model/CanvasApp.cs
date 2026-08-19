namespace PpLint.Core.Model;

public sealed class CanvasApp
{
    public required string Name { get; init; }

    public required SourceLocation Location { get; init; }

    public List<Control> Screens { get; } = [];

    public List<DataSource> DataSources { get; } = [];

    /// <summary>Expressões de nível de app, como App.OnStart.</summary>
    public List<PowerFxProperty> AppProperties { get; } = [];

    public IEnumerable<Control> AllControls() =>
        Screens.SelectMany(s => s.SelfAndDescendants());
}
