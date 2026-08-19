namespace PpLint.Core.Model;

/// <summary>
/// A cadência de um gatilho agendado: "Day", "Hour", "Minute" ou "Second",
/// e de quantos em quantos. Frequência sem intervalo declarado é de um em um.
/// </summary>
public sealed record FlowRecurrence(string Frequency, int Interval);

public sealed record FlowTrigger(
    string Name,
    string Type,
    SourceLocation Location,
    FlowRecurrence? Recurrence = null);

public sealed record FlowVariable(string Name, string Type, SourceLocation Location);

public sealed class FlowAction
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public required SourceLocation Location { get; init; }

    /// <summary>
    /// Descrição da ação. JSON não aceita comentário, então é aqui que vivem
    /// as diretivas de supressão do pp-lint em fluxos.
    /// </summary>
    public string? Description { get; init; }

    public List<string> RunAfter { get; } = [];

    /// <summary>
    /// Estados exigidos dos predecessores — "Succeeded", "Failed", "Skipped",
    /// "TimedOut". Qualquer coisa diferente de Succeeded é tratamento de erro.
    /// </summary>
    public List<string> RunAfterStates { get; } = [];

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

    /// <summary>Descrição do fluxo, como aparece na lista do Power Automate.</summary>
    public string? Description { get; init; }

    public FlowTrigger? Trigger { get; set; }

    public List<FlowAction> Actions { get; } = [];

    public List<FlowVariable> Variables { get; } = [];

    public IEnumerable<FlowAction> AllActions() =>
        Actions.SelectMany(a => a.SelfAndDescendants());
}
