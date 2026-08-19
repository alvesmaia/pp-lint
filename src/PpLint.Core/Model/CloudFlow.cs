namespace PpLint.Core.Model;

public sealed record FlowTrigger(string Name, string Type, SourceLocation Location);

public sealed record FlowVariable(string Name, string Type, SourceLocation Location);

public sealed class FlowAction
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public required SourceLocation Location { get; init; }

    public List<string> RunAfter { get; } = [];

    /// <summary>Todas as strings encontradas nos inputs da ação, onde vivem as expressões @{...}.</summary>
    public List<string> Expressions { get; } = [];

    public List<FlowAction> Children { get; } = [];

    public IEnumerable<FlowAction> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var d in child.SelfAndDescendants())
                yield return d;
    }
}

public sealed class CloudFlow
{
    public required string Name { get; init; }

    public required SourceLocation Location { get; init; }

    public FlowTrigger? Trigger { get; set; }

    public List<FlowAction> Actions { get; } = [];

    public List<FlowVariable> Variables { get; } = [];

    public IEnumerable<FlowAction> AllActions() =>
        Actions.SelectMany(a => a.SelfAndDescendants());
}
