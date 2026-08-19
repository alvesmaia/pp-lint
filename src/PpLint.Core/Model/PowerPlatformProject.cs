namespace PpLint.Core.Model;

/// <summary>
/// Representação intermediária de tudo que foi extraído do artefato.
/// As regras enxergam somente este modelo — nunca o formato de origem.
/// </summary>
public sealed class PowerPlatformProject
{
    public required string SourcePath { get; init; }

    public SolutionInfo? Solution { get; set; }

    public List<CanvasApp> Apps { get; } = [];

    public List<CloudFlow> Flows { get; } = [];

    public List<DataTable> Tables { get; } = [];
}
