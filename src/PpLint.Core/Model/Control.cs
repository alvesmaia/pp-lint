namespace PpLint.Core.Model;

public sealed record PowerFxProperty(string Name, string Script, SourceLocation Location);

public sealed record DataSource(string Name, string Kind, IReadOnlyList<string> Columns);

public sealed class Control
{
    public required string Name { get; init; }

    /// <summary>Nome do template do controle, como aparece no .msapp (ex.: "button", "label").</summary>
    public required string TemplateName { get; init; }

    public required SourceLocation Location { get; init; }

    public bool IsScreen { get; init; }

    public Control? Parent { get; private set; }

    public List<Control> Children { get; } = [];

    public List<PowerFxProperty> Properties { get; } = [];

    public void AddChild(Control child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>A tela que contém este controle, ou ele mesmo quando já é uma tela.</summary>
    public Control? ScreenOf()
    {
        var current = this;
        while (current is not null && !current.IsScreen)
            current = current.Parent;
        return current;
    }

    /// <summary>Este controle e todos os descendentes, em profundidade.</summary>
    public IEnumerable<Control> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var d in child.SelfAndDescendants())
                yield return d;
    }
}
