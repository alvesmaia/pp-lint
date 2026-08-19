# pp-lint Fase FL — Regras de Power Automate

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Analisar cloud flows de verdade — ordem de execução, tratamento de erro, laços aninhados e gatilho — com 8 regras novas.

**Architecture:** O IR de fluxo ganha duas informações que o extractor descartava (descrição do fluxo e detalhes da recorrência) e um grafo de execução derivado de `runAfter`, que dá ordem parcial entre ações. As regras consultam esse grafo.

**Tech Stack:** .NET 10, C# 14, xUnit, `Microsoft.PowerFx.Core`, `Tomlyn`.

**Spec:** `docs/superpowers/specs/2026-08-18-pp-lint-design.md` (seção 7.3)

## Por que agora

Até esta fase, o caminho de solução exportada nunca tinha visto um artefato real. Com
`tests/fixtures/solucao-exemplo.zip` (PnP, MIT) isso mudou: é um fluxo de produção com
10 ações de topo, dois laços, gatilho diário — e **nenhum tratamento de erro**. As regras
abaixo foram desenhadas contra esse formato, não contra suposição.

## Decisões desta fase

1. **Ordem de execução vem de `runAfter`.** É a única ordem que o formato expressa: cada
   ação declara de quais depende e em que estado. Ações sem `runAfter` são as primeiras.
2. **FL212 (ação desabilitada) fica de fora.** O formato de uma ação desabilitada não
   apareceu no artefato real e não vou adivinhá-lo.
3. **FL220/FL221 (consulta sem `$filter`/`Top`) ficam de fora.** Exigem mapear
   `operationId` por conector, o que é tabela de dados e merece fase própria.
4. **FL230 usa limite configurável**, com default de 15 minutos — abaixo disso, um fluxo
   agendado costuma ser sintoma de polling que deveria ser gatilho.

## Global Constraints

- **.NET 10**, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **Somente-leitura absoluto.**
- **Dependências de runtime:** apenas `Microsoft.PowerFx.Core` e `Tomlyn`.
- **Mensagens em português do Brasil.**
- **IDs de regra são imutáveis.**
- **Invariante do índice:** no máximo uma violação por alvo avaliado.
- **Toda regra chama `ctx.Evaluated(n)`.**
- **Regra nova precisa de teste positivo e negativo.**
- **Validação final contra `tests/fixtures/solucao-exemplo.zip`**, com cada achado conferido à mão.
- **TDD obrigatório.** Commit ao fim de cada task.

## Estrutura de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/PpLint.Core/Model/CloudFlow.cs` | *(modificar)* `Description` do fluxo e `Recurrence` do gatilho |
| `src/PpLint.Extractors/FlowExtractor.cs` | *(modificar)* extrair os dois |
| `src/PpLint.Core/Model/FlowExecutionGraph.cs` | Ordem parcial derivada de `runAfter` |
| `src/PpLint.Rules/Flow/ErrorHandlingRules.cs` | FL210, FL211 |
| `src/PpLint.Rules/Flow/ExecutionOrderRules.cs` | FL202, FL241 |
| `src/PpLint.Rules/Flow/UnusedOutputRule.cs` | FL203 |
| `src/PpLint.Rules/Flow/NestedLoopRule.cs` | FL222 |
| `src/PpLint.Rules/Flow/TriggerRules.cs` | FL230, FL240 |

---

### Task 1: IR do fluxo — descrição e recorrência

**Files:**
- Modify: `src/PpLint.Core/Model/CloudFlow.cs`
- Modify: `src/PpLint.Extractors/FlowExtractor.cs`
- Test: `tests/PpLint.Extractors.Tests/FlowExtractorTests.cs` (acrescentar classe ao fim)

**Interfaces:**
- Produces: `CloudFlow.Description` (`string?`); `sealed record FlowRecurrence(string Frequency, int Interval)`; `FlowTrigger.Recurrence` (`FlowRecurrence?`).

O gatilho real traz `"recurrence": {"frequency":"Day","interval":1,...}`. A descrição do
fluxo fica em `properties.description` — ausente no artefato real, o que já é um achado.

- [ ] **Step 1: Escrever o teste que falha**

Acrescentar ao fim de `tests/PpLint.Extractors.Tests/FlowExtractorTests.cs`:

```csharp
public class FlowMetadataTests
{
    [Fact]
    public void Extract_ReadsRecurrenceFromTrigger()
    {
        const string json = """
        {
          "properties": {
            "definition": {
              "triggers": {
                "Recorrencia": {
                  "type": "Recurrence",
                  "recurrence": { "frequency": "Day", "interval": 1, "timeZone": "UTC" }
                }
              },
              "actions": {}
            }
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Equal("Day", flow.Trigger!.Recurrence!.Frequency);
        Assert.Equal(1, flow.Trigger.Recurrence.Interval);
    }

    [Fact]
    public void Extract_TriggerWithoutRecurrenceHasNull()
    {
        const string json = """
        { "definition": { "triggers": { "Quando": { "type": "OpenApiConnection" } }, "actions": {} } }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Null(flow.Trigger!.Recurrence);
    }

    [Fact]
    public void Extract_ReadsFlowDescription()
    {
        const string json = """
        {
          "properties": {
            "description": "Envia o resumo diário",
            "definition": { "actions": {} }
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Equal("Envia o resumo diário", flow.Description);
    }

    [Fact]
    public void Extract_FlowWithoutDescriptionHasNull()
    {
        const string json = """
        { "properties": { "definition": { "actions": {} } } }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Null(flow.Description);
    }

    [Fact]
    public void Extract_IntervalDefaultsToOneWhenAbsent()
    {
        const string json = """
        {
          "definition": {
            "triggers": { "R": { "type": "Recurrence", "recurrence": { "frequency": "Hour" } } },
            "actions": {}
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Equal(1, flow.Trigger!.Recurrence!.Interval);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter FlowMetadataTests`
Expected: FALHA de compilação — `Recurrence` e `Description` não existem.

- [ ] **Step 3: Ampliar o modelo**

Em `src/PpLint.Core/Model/CloudFlow.cs`, substituir a declaração de `FlowTrigger` e
acrescentar `Description` a `CloudFlow`:

```csharp
/// <summary>Frequência de um gatilho agendado — "Day", "Hour", "Minute", "Second".</summary>
public sealed record FlowRecurrence(string Frequency, int Interval);

public sealed record FlowTrigger(
    string Name,
    string Type,
    SourceLocation Location,
    FlowRecurrence? Recurrence = null);
```

E dentro de `CloudFlow`, logo abaixo de `Location`:

```csharp
    /// <summary>Descrição do fluxo, como aparece na lista do Power Automate.</summary>
    public string? Description { get; init; }
```

- [ ] **Step 4: Extrair os dois campos**

Em `src/PpLint.Extractors/FlowExtractor.cs`, dentro de `Extract`, trocar a construção do
`CloudFlow` e do `FlowTrigger`:

```csharp
            var flow = new CloudFlow
            {
                Name = flowName,
                Description = FlowDescription(doc.RootElement),
                Location = new SourceLocation(artifactPath, entryPath, flowName, 0, 0),
            };
```

```csharp
                foreach (var t in triggers.EnumerateObject())
                {
                    flow.Trigger = new FlowTrigger(
                        t.Name,
                        GetString(t.Value, "type") ?? string.Empty,
                        new SourceLocation(artifactPath, entryPath, t.Name, 0, 0),
                        ReadRecurrence(t.Value));
                    break; // um fluxo tem exatamente um trigger
                }
```

E acrescentar os dois helpers ao fim da classe:

```csharp
    /// <summary>A descrição fica em properties.description, fora da definition.</summary>
    private static string? FlowDescription(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("properties", out var props)
        && props.ValueKind == JsonValueKind.Object
            ? GetString(props, "description")
            : null;

    private static FlowRecurrence? ReadRecurrence(JsonElement trigger)
    {
        if (!trigger.TryGetProperty("recurrence", out var recurrence)
            || recurrence.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var frequency = GetString(recurrence, "frequency");
        if (frequency is null)
            return null;

        var interval = recurrence.TryGetProperty("interval", out var i)
                       && i.ValueKind == JsonValueKind.Number
            ? i.GetInt32()
            : 1;

        return new FlowRecurrence(frequency, interval);
    }
```

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — os 5 novos e todos os anteriores. `FlowTrigger` ganhou um parâmetro com
valor padrão, então as construções existentes continuam válidas.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: extrair descrição do fluxo e recorrência do gatilho"
```

---

### Task 2: Grafo de execução do fluxo

**Files:**
- Create: `src/PpLint.Core/Model/FlowExecutionGraph.cs`
- Test: `tests/PpLint.Core.Tests/FlowExecutionGraphTests.cs`

**Interfaces:**
- Consumes: `CloudFlow`, `FlowAction`.
- Produces: `FlowExecutionGraph` com `static FlowExecutionGraph Build(CloudFlow flow)`, `bool RunsBefore(string a, string b)`, `IReadOnlyList<string> UnknownPredecessors`, `bool HandlesFailure`.

**`RunsBefore`** é o fecho transitivo de `runAfter`: A roda antes de B se B depende de A,
direta ou indiretamente. Ações em ramos independentes não têm ordem entre si — e a regra
que usa isso precisa tratar "não sei" como "não acuse".

**`HandlesFailure`** é verdadeiro quando alguma ação declara depender de um estado que não
seja `Succeeded` (`Failed`, `Skipped`, `TimedOut`) — é assim que o Power Automate expressa
"faça isto se aquilo falhar".

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/FlowExecutionGraphTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Core.Tests;

public class FlowExecutionGraphTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static CloudFlow FlowWith(params (string Name, string[] RunAfter, string State)[] actions)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };

        foreach (var (name, runAfter, state) in actions)
        {
            var action = new FlowAction { Name = name, Type = "Compose", Location = Loc(name) };
            action.RunAfter.AddRange(runAfter);
            if (state != "Succeeded")
                action.RunAfterStates.Add(state);
            flow.Actions.Add(action);
        }

        return flow;
    }

    [Fact]
    public void DirectDependencyOrdersActions()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("B", ["A"], "Succeeded")));

        Assert.True(graph.RunsBefore("A", "B"));
        Assert.False(graph.RunsBefore("B", "A"));
    }

    [Fact]
    public void OrderIsTransitive()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("B", ["A"], "Succeeded"),
            ("C", ["B"], "Succeeded")));

        Assert.True(graph.RunsBefore("A", "C"));
    }

    [Fact]
    public void IndependentActionsHaveNoOrder()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("B", [], "Succeeded")));

        Assert.False(graph.RunsBefore("A", "B"));
        Assert.False(graph.RunsBefore("B", "A"));
    }

    [Fact]
    public void CycleDoesNotHang()
    {
        // O formato não deveria permitir, mas um arquivo corrompido pode trazer.
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", ["B"], "Succeeded"),
            ("B", ["A"], "Succeeded")));

        Assert.True(graph.RunsBefore("A", "B"));
    }

    [Fact]
    public void UnknownPredecessorIsReported()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", ["NaoExiste"], "Succeeded")));

        Assert.Equal(["NaoExiste"], graph.UnknownPredecessors);
    }

    [Fact]
    public void FlowWithOnlySucceededDoesNotHandleFailure()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("B", ["A"], "Succeeded")));

        Assert.False(graph.HandlesFailure);
    }

    [Fact]
    public void FlowWithFailedBranchHandlesFailure()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("Avisa", ["A"], "Failed")));

        Assert.True(graph.HandlesFailure);
    }

    [Fact]
    public void NestedActionsAreIncluded()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        var loop = new FlowAction { Name = "Loop", Type = "Foreach", Location = Loc("Loop") };
        var inner = new FlowAction { Name = "Interna", Type = "Compose", Location = Loc("Interna") };
        inner.RunAfterStates.Add("Failed");
        loop.Children.Add(inner);
        flow.Actions.Add(loop);

        Assert.True(FlowExecutionGraph.Build(flow).HandlesFailure);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter FlowExecutionGraphTests`
Expected: FALHA de compilação — `FlowExecutionGraph` e `FlowAction.RunAfterStates` não existem.

- [ ] **Step 3: Guardar os estados de runAfter no IR**

O extractor hoje guarda só os nomes dos predecessores; o estado é o que revela tratamento
de erro. Em `src/PpLint.Core/Model/CloudFlow.cs`, dentro de `FlowAction`, abaixo de `RunAfter`:

```csharp
    /// <summary>
    /// Estados exigidos dos predecessores — "Succeeded", "Failed", "Skipped",
    /// "TimedOut". Qualquer coisa diferente de Succeeded é tratamento de erro.
    /// </summary>
    public List<string> RunAfterStates { get; } = [];
```

E em `src/PpLint.Extractors/FlowExtractor.cs`, dentro de `ReadActions`, trocar a leitura de
`runAfter`:

```csharp
            if (property.Value.TryGetProperty("runAfter", out var runAfter)
                && runAfter.ValueKind == JsonValueKind.Object)
            {
                foreach (var predecessor in runAfter.EnumerateObject())
                {
                    action.RunAfter.Add(predecessor.Name);

                    if (predecessor.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var estado in predecessor.Value.EnumerateArray())
                        {
                            var texto = estado.GetString();
                            if (!string.IsNullOrEmpty(texto))
                                action.RunAfterStates.Add(texto);
                        }
                    }
                }
            }
```

- [ ] **Step 4: Implementar o grafo**

`src/PpLint.Core/Model/FlowExecutionGraph.cs`:

```csharp
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
```

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — 8 testes novos e nenhuma regressão.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: grafo de execução do fluxo a partir de runAfter"
```

---

### Task 3: FL210 e FL240 — tratamento de erro e descrição

**Files:**
- Create: `src/PpLint.Rules/Flow/ErrorHandlingRules.cs`, `src/PpLint.Rules/Flow/TriggerRules.cs`
- Test: `tests/PpLint.Rules.Tests/FlowQualityRuleTests.cs`

**Interfaces:**
- Consumes: `FlowExecutionGraph`, `CloudFlow`, `IRule`, `LintContext`.
- Produces: `NoErrorHandlingRule` (FL210, Flow, Error), `FlowWithoutDescriptionRule` (FL240, Flow, Info).

**FL210** é `Error` porque um fluxo sem tratamento de falha erra em silêncio: ninguém é
avisado, e o problema aparece dias depois como dado faltando. O fluxo real do fixture tem
esse defeito.

Fluxo com uma ação só não é avaliado: não há o que encadear.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/FlowQualityRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;

namespace PpLint.Rules.Tests;

public class FlowQualityRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static PowerPlatformProject ProjectWith(CloudFlow flow)
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);
        return project;
    }

    private static CloudFlow FlowWith(
        string? description,
        params (string Name, string[] RunAfter, string State)[] actions)
    {
        var flow = new CloudFlow { Name = "AprovarPedido", Description = description, Location = Loc("AprovarPedido") };

        foreach (var (name, runAfter, state) in actions)
        {
            var action = new FlowAction { Name = name, Type = "Compose", Location = Loc(name) };
            action.RunAfter.AddRange(runAfter);
            action.RunAfterStates.Add(state);
            flow.Actions.Add(action);
        }

        return flow;
    }

    private static LintResult Run(IRule rule, CloudFlow flow) =>
        new RuleEngine([rule]).Run(ProjectWith(flow), PpLintConfig.Default);

    [Fact]
    public void FL210_ReportsFlowWithoutFailureBranch()
    {
        var flow = FlowWith(null, ("A", [], "Succeeded"), ("B", ["A"], "Succeeded"));
        var d = Assert.Single(Run(new NoErrorHandlingRule(), flow).Diagnostics);

        Assert.Equal("FL210", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("AprovarPedido", d.Message);
    }

    [Fact]
    public void FL210_AcceptsFlowWithFailureBranch()
    {
        var flow = FlowWith(null, ("A", [], "Succeeded"), ("Avisa", ["A"], "Failed"));

        Assert.Empty(Run(new NoErrorHandlingRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL210_IgnoresSingleActionFlow()
    {
        // Sem encadeamento não há o que tratar.
        var flow = FlowWith(null, ("A", [], "Succeeded"));
        var result = Run(new NoErrorHandlingRule(), flow);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void FL210_EvaluatesOneTargetPerFlow()
    {
        var flow = FlowWith(null, ("A", [], "Succeeded"), ("B", ["A"], "Succeeded"));

        Assert.Equal(1, Assert.Single(Run(new NoErrorHandlingRule(), flow).Tallies).Evaluated);
    }

    [Fact]
    public void FL240_ReportsFlowWithoutDescription()
    {
        var flow = FlowWith(null, ("A", [], "Succeeded"));
        var d = Assert.Single(Run(new FlowWithoutDescriptionRule(), flow).Diagnostics);

        Assert.Equal("FL240", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void FL240_AcceptsFlowWithDescription()
    {
        var flow = FlowWith("Envia o resumo diário", ("A", [], "Succeeded"));

        Assert.Empty(Run(new FlowWithoutDescriptionRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL240_TreatsBlankDescriptionAsMissing()
    {
        var flow = FlowWith("   ", ("A", [], "Succeeded"));

        Assert.Single(Run(new FlowWithoutDescriptionRule(), flow).Diagnostics);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter FlowQualityRuleTests`
Expected: FALHA de compilação — as duas regras não existem.

- [ ] **Step 3: Implementar FL210**

`src/PpLint.Rules/Flow/ErrorHandlingRules.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL210 — nenhuma ação do fluxo trata falha. Quando algo dá errado, o fluxo
/// morre em silêncio: ninguém é avisado, e o problema aparece dias depois como
/// dado faltando. Basta uma ação com 'Configurar execução após' cobrindo
/// falha, ou um escopo de tratamento.
/// </summary>
[Rule("FL210", RuleCategory.Flow, Severity.Error)]
public sealed class NoErrorHandlingRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            // Fluxo com uma ação só não encadeia nada; não há o que tratar.
            if (flow.AllActions().Count() < 2)
                continue;

            ctx.Evaluated(1);

            if (!FlowExecutionGraph.Build(flow).HandlesFailure)
            {
                ctx.Report(
                    flow.Location,
                    $"O fluxo '{flow.Name}' não trata falha em nenhuma ação. "
                    + "Configure 'Executar após' com falha em alguma etapa, ou envolva o "
                    + "trecho crítico num escopo com tratamento.");
            }
        }
    }
}
```

- [ ] **Step 4: Implementar FL240**

`src/PpLint.Rules/Flow/TriggerRules.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL240 — fluxo sem descrição. Quem abre a lista do Power Automate meses depois
/// só tem o nome para entender o que a automação faz.
/// </summary>
[Rule("FL240", RuleCategory.Flow, Severity.Info)]
public sealed class FlowWithoutDescriptionRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            ctx.Evaluated(1);

            if (string.IsNullOrWhiteSpace(flow.Description))
            {
                ctx.Report(
                    flow.Location,
                    $"O fluxo '{flow.Name}' não tem descrição. Explique em uma linha o que ele faz "
                    + "e quando dispara.");
            }
        }
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter FlowQualityRuleTests`
Expected: PASSA — 7 testes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: regras FL210 e FL240 de tratamento de erro e descrição"
```

---

### Task 4: FL202 e FL241 — ordem de execução

**Files:**
- Create: `src/PpLint.Rules/Flow/ExecutionOrderRules.cs`
- Test: `tests/PpLint.Rules.Tests/ExecutionOrderRuleTests.cs`

**Interfaces:**
- Consumes: `FlowExecutionGraph`, `CloudFlow`, `IRule`, `LintContext`.
- Produces: `VariableUsedBeforeInitializationRule` (FL202, Flow, Error), `UnknownPredecessorRule` (FL241, Flow, Error).

**FL202** só acusa quando a ordem é conhecida e está invertida: a leitura roda **antes** da
inicialização. Ações sem ordem entre si não são acusadas — o formato não diz o que acontece
primeiro, e chutar geraria falso positivo.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/ExecutionOrderRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;

namespace PpLint.Rules.Tests;

public class ExecutionOrderRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static FlowAction Action(string name, string type, string[] runAfter, params string[] expressions)
    {
        var action = new FlowAction { Name = name, Type = type, Location = Loc(name) };
        action.RunAfter.AddRange(runAfter);
        action.RunAfterStates.Add("Succeeded");
        action.Expressions.AddRange(expressions);
        return action;
    }

    private static LintResult Run(IRule rule, CloudFlow flow)
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);
        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void FL202_ReportsReadBeforeInitialization()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("Usa", "Compose", [], "@{variables('varTotal')}"));
        flow.Actions.Add(Action("Inicializa", "InitializeVariable", ["Usa"]));
        flow.Variables.Add(new FlowVariable("varTotal", "integer", Loc("Inicializa")));

        var d = Assert.Single(Run(new VariableUsedBeforeInitializationRule(), flow).Diagnostics);

        Assert.Equal("FL202", d.RuleId);
        Assert.Contains("varTotal", d.Message);
    }

    [Fact]
    public void FL202_AcceptsCorrectOrder()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("Inicializa", "InitializeVariable", []));
        flow.Actions.Add(Action("Usa", "Compose", ["Inicializa"], "@{variables('varTotal')}"));
        flow.Variables.Add(new FlowVariable("varTotal", "integer", Loc("Inicializa")));

        Assert.Empty(Run(new VariableUsedBeforeInitializationRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL202_StaysQuietWhenOrderIsUnknown()
    {
        // Ramos independentes: o formato não diz qual roda primeiro.
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("Inicializa", "InitializeVariable", []));
        flow.Actions.Add(Action("Usa", "Compose", [], "@{variables('varTotal')}"));
        flow.Variables.Add(new FlowVariable("varTotal", "integer", Loc("Inicializa")));

        Assert.Empty(Run(new VariableUsedBeforeInitializationRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL202_EvaluatesOneTargetPerVariable()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("Inicializa", "InitializeVariable", []));
        flow.Actions.Add(Action("Usa", "Compose", ["Inicializa"], "@{variables('varTotal')}"));
        flow.Variables.Add(new FlowVariable("varTotal", "integer", Loc("Inicializa")));

        Assert.Equal(1, Assert.Single(Run(new VariableUsedBeforeInitializationRule(), flow).Tallies).Evaluated);
    }

    [Fact]
    public void FL241_ReportsRunAfterPointingToNothing()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("A", "Compose", ["AcaoQueNaoExiste"]));

        var d = Assert.Single(Run(new UnknownPredecessorRule(), flow).Diagnostics);

        Assert.Equal("FL241", d.RuleId);
        Assert.Contains("AcaoQueNaoExiste", d.Message);
    }

    [Fact]
    public void FL241_AcceptsValidReferences()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("A", "Compose", []));
        flow.Actions.Add(Action("B", "Compose", ["A"]));

        Assert.Empty(Run(new UnknownPredecessorRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL241_EvaluatesOneTargetPerFlow()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("A", "Compose", []));

        Assert.Equal(1, Assert.Single(Run(new UnknownPredecessorRule(), flow).Tallies).Evaluated);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter ExecutionOrderRuleTests`
Expected: FALHA de compilação — as duas regras não existem.

- [ ] **Step 3: Implementar**

`src/PpLint.Rules/Flow/ExecutionOrderRules.cs`:

```csharp
using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL202 — uma ação lê a variável antes da ação que a inicializa. No Power
/// Automate isso é erro de execução, não valor vazio.
///
/// Só acusa quando a ordem é conhecida e está invertida: ações em ramos
/// independentes não têm ordem no formato, e chutar geraria falso positivo.
/// </summary>
[Rule("FL202", RuleCategory.Flow, Severity.Error)]
public sealed class VariableUsedBeforeInitializationRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            var graph = FlowExecutionGraph.Build(flow);
            var acoes = flow.AllActions().ToList();

            foreach (var variable in flow.Variables)
            {
                ctx.Evaluated(1);

                var inicializadora = acoes.FirstOrDefault(a =>
                    a.Type.Equals("InitializeVariable", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Location.Symbol, variable.Location.Symbol, StringComparison.OrdinalIgnoreCase));

                if (inicializadora is null)
                    continue;

                var leitoraAnterior = acoes.FirstOrDefault(a =>
                    !ReferenceEquals(a, inicializadora)
                    && LeVariavel(a, variable.Name)
                    && graph.RunsBefore(a.Name, inicializadora.Name));

                if (leitoraAnterior is not null)
                {
                    ctx.Report(
                        leitoraAnterior.Location,
                        $"A ação '{leitoraAnterior.Name}' lê a variável '{variable.Name}' antes de "
                        + $"'{inicializadora.Name}' inicializá-la. No Power Automate isso falha em execução.");
                }
            }
        }
    }

    private static bool LeVariavel(FlowAction action, string nome)
    {
        var padrao = new Regex(
            $@"variables\(\s*'{Regex.Escape(nome)}'\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return action.Expressions.Any(padrao.IsMatch);
    }
}

/// <summary>
/// FL241 — 'Executar após' aponta para uma ação que não existe no fluxo. O
/// arquivo está inconsistente: costuma acontecer quando alguém edita o JSON à
/// mão ou renomeia uma ação sem atualizar quem dependia dela.
/// </summary>
[Rule("FL241", RuleCategory.Flow, Severity.Error)]
public sealed class UnknownPredecessorRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            ctx.Evaluated(1);

            var desconhecidos = FlowExecutionGraph.Build(flow).UnknownPredecessors;

            if (desconhecidos.Count > 0)
            {
                ctx.Report(
                    flow.Location,
                    $"O fluxo '{flow.Name}' depende de ações que não existem: "
                    + $"{string.Join(", ", desconhecidos)}.");
            }
        }
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter ExecutionOrderRuleTests`
Expected: PASSA — 7 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: regras FL202 e FL241 de ordem de execução"
```

---

### Task 5: FL222 e FL230 — laços aninhados e recorrência

**Files:**
- Create: `src/PpLint.Rules/Flow/NestedLoopRule.cs`
- Modify: `src/PpLint.Rules/Flow/TriggerRules.cs`
- Modify: `src/PpLint.Core/PpLintConfig.cs` (limiar de recorrência)
- Test: `tests/PpLint.Rules.Tests/FlowStructureRuleTests.cs`

**Interfaces:**
- Consumes: `CloudFlow`, `FlowAction`, `FlowRecurrence`, `IRule`, `LintContext`.
- Produces: `NestedLoopRule` (FL222, Flow, Warning), `FrequentRecurrenceRule` (FL230, Flow, Warning); `PpLintConfig.Thresholds` com `MinRecurrenceMinutes` (default 15).

**FL222.** Um `Apply to each` dentro de outro multiplica chamadas: cem itens dentro de cem
viram dez mil execuções, e o fluxo estoura o limite de ações.

**FL230.** Recorrência abaixo do limiar quase sempre é *polling* que deveria ser gatilho de
evento — consome execução da licença sem necessidade.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/FlowStructureRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;

namespace PpLint.Rules.Tests;

public class FlowStructureRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static LintResult Run(IRule rule, CloudFlow flow, PpLintConfig? config = null)
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);
        return new RuleEngine([rule]).Run(project, config ?? PpLintConfig.Default);
    }

    private static FlowAction Loop(string name, params FlowAction[] children)
    {
        var loop = new FlowAction { Name = name, Type = "Foreach", Location = Loc(name) };
        loop.Children.AddRange(children);
        return loop;
    }

    private static CloudFlow FlowWith(params FlowAction[] actions)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.AddRange(actions);
        return flow;
    }

    private static CloudFlow FlowWithRecurrence(string frequency, int interval)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Trigger = new FlowTrigger("R", "Recurrence", Loc("R"), new FlowRecurrence(frequency, interval));
        return flow;
    }

    [Fact]
    public void FL222_ReportsNestedLoop()
    {
        var interno = Loop("Interno");
        var d = Assert.Single(Run(new NestedLoopRule(), FlowWith(Loop("Externo", interno))).Diagnostics);

        Assert.Equal("FL222", d.RuleId);
        Assert.Contains("Interno", d.Message);
    }

    [Fact]
    public void FL222_AcceptsSiblingLoops()
    {
        // Dois laços lado a lado não se multiplicam.
        Assert.Empty(Run(new NestedLoopRule(), FlowWith(Loop("A"), Loop("B"))).Diagnostics);
    }

    [Fact]
    public void FL222_AcceptsLoopWithOrdinaryChildren()
    {
        var interna = new FlowAction { Name = "Compor", Type = "Compose", Location = Loc("Compor") };

        Assert.Empty(Run(new NestedLoopRule(), FlowWith(Loop("Externo", interna))).Diagnostics);
    }

    [Fact]
    public void FL222_EvaluatesEveryLoop()
    {
        var result = Run(new NestedLoopRule(), FlowWith(Loop("A"), Loop("B", Loop("C"))));
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(3, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void FL230_ReportsEveryFiveMinutes()
    {
        var d = Assert.Single(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Minute", 5)).Diagnostics);

        Assert.Equal("FL230", d.RuleId);
        Assert.Contains("5", d.Message);
    }

    [Fact]
    public void FL230_ReportsEverySecond()
    {
        Assert.Single(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Second", 30)).Diagnostics);
    }

    [Fact]
    public void FL230_AcceptsDailyRecurrence() =>
        Assert.Empty(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Day", 1)).Diagnostics);

    [Fact]
    public void FL230_AcceptsHourlyRecurrence() =>
        Assert.Empty(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Hour", 1)).Diagnostics);

    [Fact]
    public void FL230_IgnoresFlowWithoutRecurrence()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Trigger = new FlowTrigger("Quando", "OpenApiConnection", Loc("Quando"));

        var result = Run(new FrequentRecurrenceRule(), flow);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void FL230_RespectsConfiguredThreshold()
    {
        var config = PpLintConfig.Default with
        {
            Thresholds = new ThresholdConfig { MinRecurrenceMinutes = 1 },
        };

        Assert.Empty(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Minute", 5), config).Diagnostics);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter FlowStructureRuleTests`
Expected: FALHA de compilação — as regras e `ThresholdConfig` não existem.

- [ ] **Step 3: Acrescentar o limiar à configuração e ao leitor de TOML**

Em `src/PpLint.Core/PpLintConfig.cs`, antes de `PpLintConfig`:

```csharp
/// <summary>Limiares numéricos das regras. Sobrescrevíveis em [pp-lint.thresholds].</summary>
public sealed record ThresholdConfig
{
    /// <summary>
    /// Recorrência mais frequente que isto costuma ser polling que deveria ser
    /// gatilho de evento, e consome execução da licença sem necessidade.
    /// </summary>
    public int MinRecurrenceMinutes { get; init; } = 15;
}
```

E dentro de `PpLintConfig`, junto das demais propriedades:

```csharp
    public ThresholdConfig Thresholds { get; init; } = new();
```

A seção precisa ser aceita pelo leitor, senão documentá-la no arquivo de exemplo
seria documentar um erro. Em `src/PpLint.Core/Configuration/ConfigFile.cs`,
acrescentar a `ConfigFile`:

```csharp
    public int? MinRecurrenceMinutes { get; init; }
```

Em `src/PpLint.Core/Configuration/TomlConfigReader.cs`, acrescentar "thresholds"
a `KnownSectionKeys`, e ler a seção dentro de `Read`:

```csharp
            MinRecurrenceMinutes = GetInt(GetTable(section, "thresholds"), "min-recurrence-minutes"),
```

com o helper:

```csharp
    private static int? GetInt(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
            return null;

        return value is long numero
            ? (int)numero
            : throw new ConfigException( '{key}' precisa ser um número inteiro.");
    }
```

E em `src/PpLint.Core/Configuration/ConfigResolver.cs`, dentro do objeto devolvido
por `Resolve`:

```csharp
            Thresholds = new ThresholdConfig
            {
                MinRecurrenceMinutes = file.MinRecurrenceMinutes ?? 15,
            },
```

- [ ] **Step 4: Implementar FL222**

`src/PpLint.Rules/Flow/NestedLoopRule.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL222 — 'Aplicar a cada' dentro de outro. Cem itens dentro de cem viram dez
/// mil execuções: o fluxo fica lento e costuma estourar o limite de ações.
/// Quase sempre dá para resolver com filtro ou consulta melhor antes do laço.
/// </summary>
[Rule("FL222", RuleCategory.Flow, Severity.Warning)]
public sealed class NestedLoopRule : IRule
{
    private static readonly string[] LoopTypes = ["Foreach", "Until", "Do_Until"];

    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            foreach (var acao in flow.AllActions())
            {
                if (!IsLoop(acao))
                    continue;

                ctx.Evaluated(1);

                var interno = acao.Children
                    .SelectMany(c => c.SelfAndDescendants())
                    .FirstOrDefault(IsLoop);

                if (interno is not null)
                {
                    ctx.Report(
                        interno.Location,
                        $"O laço '{interno.Name}' está dentro de '{acao.Name}'. "
                        + "Laços aninhados multiplicam as execuções e costumam estourar o "
                        + "limite de ações; filtre ou agregue os dados antes do laço externo.");
                }
            }
        }
    }

    private static bool IsLoop(FlowAction action) =>
        LoopTypes.Contains(action.Type, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 5: Implementar FL230**

Acrescentar ao fim de `src/PpLint.Rules/Flow/TriggerRules.cs`:

```csharp
/// <summary>
/// FL230 — gatilho agendado mais frequente que o limiar. Costuma ser polling
/// onde caberia um gatilho de evento, e cada execução consome cota da licença
/// mesmo quando não há nada a fazer.
/// </summary>
[Rule("FL230", RuleCategory.Flow, Severity.Warning)]
public sealed class FrequentRecurrenceRule : IRule
{
    public void Check(LintContext ctx)
    {
        var limite = ctx.Config.Thresholds.MinRecurrenceMinutes;

        foreach (var flow in ctx.Project.Flows)
        {
            var recorrencia = flow.Trigger?.Recurrence;
            if (recorrencia is null)
                continue;

            ctx.Evaluated(1);

            var minutos = EmMinutos(recorrencia);
            if (minutos is not null && minutos < limite)
            {
                ctx.Report(
                    flow.Trigger!.Location,
                    $"O fluxo '{flow.Name}' roda a cada {recorrencia.Interval} {recorrencia.Frequency} "
                    + $"(menos de {limite} minutos). Considere um gatilho de evento no lugar do agendamento.");
            }
        }
    }

    /// <summary>Intervalo em minutos; null para frequências que não cabem em minutos.</summary>
    private static double? EmMinutos(PpLint.Core.Model.FlowRecurrence recorrencia) =>
        recorrencia.Frequency.ToLowerInvariant() switch
        {
            "second" => recorrencia.Interval / 60.0,
            "minute" => recorrencia.Interval,
            "hour" => recorrencia.Interval * 60.0,
            "day" => recorrencia.Interval * 1440.0,
            "week" => recorrencia.Interval * 10080.0,
            "month" => recorrencia.Interval * 43200.0,
            _ => null,
        };
}
```

- [ ] **Step 6: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter FlowStructureRuleTests`
Expected: PASSA — 10 testes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: regras FL222 e FL230 de laços aninhados e recorrência"
```

---

### Task 6: FL203 — saída de ação nunca usada

**Files:**
- Create: `src/PpLint.Rules/Flow/UnusedOutputRule.cs`
- Test: `tests/PpLint.Rules.Tests/UnusedOutputRuleTests.cs`

**Interfaces:**
- Consumes: `CloudFlow`, `FlowAction`, `IRule`, `LintContext`.
- Produces: `UnusedOutputRule` (FL203, Flow, Warning).

Só ações **puramente computacionais** entram: `Compose`, `Select`, `Filter array`,
`Parse JSON`. Uma chamada de conector pode existir pelo efeito colateral — enviar e-mail,
gravar registro — e cobrar uso da saída dela seria errado.

O uso é reconhecido por `outputs('Nome')`, `body('Nome')` ou `@Nome` nas expressões de
qualquer ação.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/UnusedOutputRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;

namespace PpLint.Rules.Tests;

public class UnusedOutputRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static FlowAction Action(string name, string type, params string[] expressions)
    {
        var action = new FlowAction { Name = name, Type = type, Location = Loc(name) };
        action.Expressions.AddRange(expressions);
        return action;
    }

    private static LintResult Run(params FlowAction[] actions)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.AddRange(actions);

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        return new RuleEngine([new UnusedOutputRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsComposeNeverConsumed()
    {
        var d = Assert.Single(Run(
            Action("Compor", "Compose", "valor"),
            Action("Outra", "Compose", "nada a ver")).Diagnostics);

        Assert.Equal("FL203", d.RuleId);
        Assert.Contains("Compor", d.Message);
    }

    [Fact]
    public void AcceptsOutputConsumedByOutputs() =>
        Assert.Empty(Run(
            Action("Compor", "Compose", "valor"),
            Action("Usa", "Compose", "@{outputs('Compor')}")).Diagnostics);

    [Fact]
    public void AcceptsOutputConsumedByBody() =>
        Assert.Empty(Run(
            Action("Analisa", "ParseJson", "{}"),
            Action("Usa", "Compose", "@{body('Analisa')}")).Diagnostics);

    [Fact]
    public void IgnoresConnectorActions()
    {
        // Enviar e-mail existe pelo efeito, não pela saída.
        var result = Run(
            Action("Enviar_email", "OpenApiConnection", "assunto"),
            Action("Outra", "Compose", "x"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("Enviar_email"));
    }

    [Fact]
    public void IgnoresVariableActions()
    {
        var result = Run(
            Action("Inicializa", "InitializeVariable", "x"),
            Action("Define", "SetVariable", "y"));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EvaluatesOneTargetPerComputationalAction()
    {
        var result = Run(
            Action("A", "Compose", "x"),
            Action("B", "Compose", "@{outputs('A')}"));

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UnusedOutputRuleTests`
Expected: FALHA de compilação — a regra não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.Rules/Flow/UnusedOutputRule.cs`:

```csharp
using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL203 — ação puramente computacional cuja saída ninguém consome. Cada ação
/// conta no limite do fluxo, e uma que não alimenta nada costuma ser resto de
/// depuração.
///
/// Só entram tipos sem efeito colateral: uma chamada de conector pode existir
/// justamente pelo efeito — enviar e-mail, gravar registro — e cobrar uso da
/// saída dela seria errado.
/// </summary>
[Rule("FL203", RuleCategory.Flow, Severity.Warning)]
public sealed class UnusedOutputRule : IRule
{
    private static readonly string[] ComputationalTypes =
        ["Compose", "Select", "Query", "ParseJson", "Join", "Table", "Csv"];

    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            var acoes = flow.AllActions().ToList();
            var todasExpressoes = acoes.SelectMany(a => a.Expressions).ToList();

            foreach (var acao in acoes)
            {
                if (!ComputationalTypes.Contains(acao.Type, StringComparer.OrdinalIgnoreCase))
                    continue;

                ctx.Evaluated(1);

                if (!Consumida(acao.Name, todasExpressoes))
                {
                    ctx.Report(
                        acao.Location,
                        $"A saída da ação '{acao.Name}' não é usada em lugar nenhum do fluxo. "
                        + "Remova a ação ou consuma o resultado.");
                }
            }
        }
    }

    /// <summary>
    /// O Power Automate referencia a saída de uma ação por outputs('Nome'),
    /// body('Nome') ou pelo atalho @Nome.
    /// </summary>
    private static bool Consumida(string nome, IReadOnlyList<string> expressoes)
    {
        var escapado = Regex.Escape(nome);
        var padrao = new Regex(
            $@"(outputs|body|actionOutputs|actionBody)\(\s*'{escapado}'\s*\)|@\{{?\s*{escapado}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return expressoes.Any(padrao.IsMatch);
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UnusedOutputRuleTests`
Expected: PASSA — 6 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: regra FL203 de saída de ação nunca usada"
```

---

### Task 7: Validação contra a solução real e documentação

**Files:**
- Modify: `tests/PpLint.Extractors.Tests/RealArtifactTests.cs`
- Modify: `README.md`, `PROGRESSO.md`, `pp-lint.example.toml`
- Modify: `tests/PpLint.Rules.Tests/RuleCatalogContractTests.cs`

**Interfaces:**
- Consumes: tudo das tasks anteriores.
- Produces: nenhuma API nova.

O fluxo real do fixture tem 10 ações de topo, dois laços irmãos, gatilho diário e nenhum
tratamento de erro. As regras precisam achar exatamente isso: **FL210 sim**, FL222 não
(os laços são irmãos), FL230 não (diário), FL241 não.

- [ ] **Step 1: Escrever o teste de validação**

Acrescentar ao fim da classe `RealArtifactTests`:

```csharp
    [SkippableFact]
    public void RealSolution_FlowRulesMatchWhatTheFileShows()
    {
        var path = SolutionFixture();
        Skip.If(path is null, "Fixture não encontrado.");

        var project = ProjectLoader.Load(path!);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        var ids = result.Diagnostics.Select(d => d.RuleId).ToList();

        // O fluxo encadeia dez ações e nenhuma trata falha — achado verificado no arquivo.
        Assert.Contains("FL210", ids);

        // Os dois laços são irmãos, não aninhados; o gatilho é diário; e todo
        // runAfter aponta para ação existente.
        Assert.DoesNotContain("FL222", ids);
        Assert.DoesNotContain("FL230", ids);
        Assert.DoesNotContain("FL241", ids);
    }
```

- [ ] **Step 2: Rodar e conferir cada achado à mão**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter FlowRulesMatch`

Depois liste tudo e confirme no arquivo:

```bash
dotnet run --project src/PpLint.Cli -c Release -- check tests/fixtures/solucao-exemplo.zip --no-color
```

Qualquer achado que não se confirme no JSON do fluxo é defeito da regra — **corrija a regra,
não o teste**.

- [ ] **Step 3: Ampliar o projeto de exemplo do contrato do catálogo**

As regras novas precisam de alvo no teste `EveryRuleCountsEvaluatedTargetsOnARepresentativeProject`.
Em `tests/PpLint.Rules.Tests/RuleCatalogContractTests.cs`, substituir a construção do fluxo
nesse teste por:

```csharp
        var flow = new CloudFlow
        {
            Name = "F",
            Description = "fluxo de exemplo",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "F", 0, 0),
        };
        flow.Trigger = new FlowTrigger(
            "R", "Recurrence",
            new SourceLocation("teste.zip", "Workflows/f.json", "R", 0, 0),
            new FlowRecurrence("Day", 1));

        var inicializa = new FlowAction
        {
            Name = "Inicializar",
            Type = "InitializeVariable",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "Inicializar", 0, 0),
        };
        inicializa.RunAfterStates.Add("Succeeded");

        var compoe = new FlowAction
        {
            Name = "Compor",
            Type = "Compose",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "Compor", 0, 0),
        };
        compoe.RunAfter.Add("Inicializar");
        compoe.RunAfterStates.Add("Succeeded");
        compoe.Expressions.Add("@{variables('varX')}");

        var laco = new FlowAction
        {
            Name = "Laco",
            Type = "Foreach",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "Laco", 0, 0),
        };
        laco.RunAfterStates.Add("Succeeded");

        flow.Actions.Add(inicializa);
        flow.Actions.Add(compoe);
        flow.Actions.Add(laco);
        flow.Variables.Add(new FlowVariable("varX", "integer",
            new SourceLocation("teste.zip", "Workflows/f.json", "Inicializar", 0, 0)));
```

- [ ] **Step 4: Documentar as regras no README**

Acrescentar à tabela de regras, após a linha de FL201:

```markdown
| FL202 | Variável usada antes de ser inicializada | Erro |
| FL203 | Saída de ação computacional nunca consumida | Aviso |
| FL210 | Fluxo sem nenhum tratamento de falha | Erro |
| FL222 | `Aplicar a cada` dentro de outro | Aviso |
| FL230 | Recorrência mais frequente que o limiar (15 min) | Aviso |
| FL240 | Fluxo sem descrição | Informação |
| FL241 | `Executar após` aponta para ação inexistente | Erro |
```

- [ ] **Step 5: Documentar o limiar no arquivo de exemplo**

Acrescentar ao fim de `pp-lint.example.toml`:

```toml
# Limiares numéricos das regras.
[pp-lint.thresholds]
# Recorrência mais frequente que isto (em minutos) vira aviso da FL230.
# min-recurrence-minutes = 15
```

- [ ] **Step 6: Atualizar o PROGRESSO**

Acrescentar após a seção da Fase 2b-2:

```markdown
## Fase FL — concluída

Sete regras de Power Automate (FL202, FL203, FL210, FL222, FL230, FL240, FL241) e o
grafo de execução derivado de `runAfter`. O catálogo passou de 21 para 28 regras.
Plano: `docs/superpowers/plans/2026-08-19-pp-lint-fase-fl.md`.

Primeira fase desenhada contra um artefato real desde o início: o fluxo do fixture
tem 10 ações encadeadas e nenhum tratamento de falha, o que a FL210 aponta.
```

- [ ] **Step 7: Rodar tudo e commitar**

```bash
dotnet test
git add -A
git commit -m "docs: catálogo de 28 regras; test: regras de fluxo validadas contra solução real"
```

---

## Cobertura do spec nesta fase

Entregue: FL202, FL203, FL210, FL222, FL230, FL240, FL241 e o grafo de execução.

Adiado, com o motivo:

| Item | Motivo |
|---|---|
| FL211 (escopo de catch que não relata falha) | Depende de reconhecer escopos de tratamento, que o artefato real não exercita |
| FL212 (ação desabilitada) | O formato de ação desabilitada não apareceu no artefato real |
| FL220, FL221 (consulta sem `$filter`/`Top`) | Exigem tabela de `operationId` por conector — fase própria |
| FL223 (concorrência desligada) | Depende de `runtimeConfiguration`, ausente no artefato real |
| FL224 (chamada de conector em laço) | Sobreposição com FL222; melhor avaliar depois de ver mais fluxos |
| FL231 (trigger sem condição) | Precisa de mais exemplos reais para não virar ruído |
