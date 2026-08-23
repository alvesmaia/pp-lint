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

    /// <summary>
    /// Quantos itens um laço processa ao mesmo tempo. Ausente ou 1 significa
    /// sequencial, que é o padrão do Power Automate.
    /// </summary>
    public int? ConcurrencyDegree { get; init; }

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

    /// <summary>
    /// O gatilho declara condição de disparo. Sem ela o fluxo acorda a cada
    /// evento, mesmo quando vai desistir na primeira ação.
    /// </summary>
    public bool TriggerHasCondition { get; set; }

    public List<FlowAction> Actions { get; } = [];

    public List<FlowVariable> Variables { get; } = [];

    public IEnumerable<FlowAction> AllActions() =>
        Actions.SelectMany(a => a.SelfAndDescendants());
}
