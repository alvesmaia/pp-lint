namespace PpLint.Core.Model;

/// <summary>
/// Ordem de execução de um fluxo, derivada de runAfter — a única ordem que o
/// formato expressa. Ações em ramos independentes não têm ordem entre si, e
/// quem consulta precisa tratar isso como "não sei", nunca como "não acontece".
/// </summary>
public sealed class FlowExecutionGraph
{
    private readonly Dictionary<string, HashSet<string>> _predecessors;
    private readonly List<string> _unknown;

    private FlowExecutionGraph(
        Dictionary<string, HashSet<string>> predecessors,
        List<string> unknown,
        bool handlesFailure)
    {
        _predecessors = predecessors;
        _unknown = unknown;
        HandlesFailure = handlesFailure;
    }

    /// <summary>
    /// Alguma ação declara depender de um estado que não seja Succeeded. É assim
    /// que o Power Automate expressa "faça isto se aquilo falhar".
    /// </summary>
    public bool HandlesFailure { get; }

    /// <summary>Nomes citados em runAfter que não correspondem a ação nenhuma.</summary>
    public IReadOnlyList<string> UnknownPredecessors => _unknown;

    public static FlowExecutionGraph Build(CloudFlow flow)
    {
        var acoes = flow.AllActions().ToList();
        var nomes = acoes.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var predecessors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();
        var handlesFailure = false;

        foreach (var acao in acoes)
        {
            predecessors[acao.Name] = new HashSet<string>(acao.RunAfter, StringComparer.OrdinalIgnoreCase);

            foreach (var anterior in acao.RunAfter)
                if (!nomes.Contains(anterior) && !unknown.Contains(anterior, StringComparer.OrdinalIgnoreCase))
                    unknown.Add(anterior);

            if (acao.RunAfterStates.Any(e => !string.Equals(e, "Succeeded", StringComparison.OrdinalIgnoreCase)))
                handlesFailure = true;
        }

        return new FlowExecutionGraph(predecessors, unknown, handlesFailure);
    }

    /// <summary>
    /// 'antes' roda antes de 'depois'? Percurso do fecho transitivo com controle
    /// de visitados: um arquivo corrompido pode trazer ciclo, e travar o linter
    /// num ciclo seria pior que analisá-lo mal.
    /// </summary>
    public bool RunsBefore(string antes, string depois)
    {
        var visitados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fila = new Queue<string>();
        fila.Enqueue(depois);

        while (fila.Count > 0)
        {
            var atual = fila.Dequeue();
            if (!visitados.Add(atual))
                continue;

            if (!_predecessors.TryGetValue(atual, out var anteriores))
                continue;

            foreach (var anterior in anteriores)
            {
                if (string.Equals(anterior, antes, StringComparison.OrdinalIgnoreCase))
                    return true;

                fila.Enqueue(anterior);
            }
        }

        return false;
    }
}
