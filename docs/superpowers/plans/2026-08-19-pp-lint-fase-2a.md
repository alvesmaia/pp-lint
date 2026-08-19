# pp-lint Fase 2a — Configuração e Supressão

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tornar o pp-lint adotável em um app que já existe: configuração em `pp-lint.toml`, presets de nomenclatura escolhíveis, supressão de achados caso a caso e seleção de regras pela linha de comando.

**Architecture:** Duas peças novas, ambas fora do caminho das regras. `ConfigResolver` transforma defaults + preset + arquivo + flags em um `PpLintConfig` imutável, com a precedência como função pura testável. `SuppressionIndex` é construído a partir do IR, varrendo comentários das fórmulas e descrições das ações, e é consultado pelo `LintContext` antes de cada diagnóstico ser emitido. Nenhuma das 5 regras existentes é modificada.

**Tech Stack:** .NET 10, C# 14, xUnit, `Microsoft.PowerFx.Core`, `Tomlyn` (nova — segunda e última dependência de runtime).

**Spec:** `docs/superpowers/specs/2026-08-18-pp-lint-design.md` (seções 9, 11.1 e 8)

## Decisões desta fase

Tomadas em conversa com o dono do projeto, após a validação da Fase 1 contra apps reais:

1. **Presets nomeados com escolha explícita.** O app real validado usa `ButtonCreateGame` — consistente, porém diferente do preset embutido, o que gerou 129 avisos. Em vez de impor uma régua, o pp-lint passa a oferecer presets (`camel-prefix`, `pascal-type`) e o time escolhe. Sem escolha, avisa uma vez qual está usando e como trocar.
2. **Supressão inline mais ignores por glob**, sem baseline. Baseline continua na fase de release.
3. **Tomlyn** para ler TOML, usando `Toml.ToModel` e leitura manual da tabela — sem mapeamento por reflexão, que quebraria se o trimming voltar.
4. **Precedência** defaults → preset → arquivo → flags de CLI, como ruff e ESLint.

## Global Constraints

- **.NET 10** (`net10.0`), `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **Somente-leitura absoluto.** Nada fora de `--output` é aberto para escrita.
- **Dependências de runtime:** apenas `Microsoft.PowerFx.Core` e `Tomlyn`. Qualquer outra exige justificativa.
- **Mensagens em português do Brasil**, com acentuação correta.
- **IDs de regra são imutáveis.**
- **Invariante do índice:** no máximo uma violação por alvo avaliado (`V(r) ≤ E(r)`).
- **Achado suprimido sai do numerador e do denominador** do índice — suprimir não pode inflar a conformidade.
- **Regras não conhecem supressão.** Elas chamam `ctx.Report`; o contexto decide se o achado sobrevive.
- **Config inválida é erro de execução** (exit code 2) com mensagem que nomeia o arquivo e o problema — nunca ignorada em silêncio.
- **TDD obrigatório.** Commit ao fim de cada task.

## Estrutura de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/PpLint.Core/Model/CloudFlow.cs` | *(modificar)* `FlowAction.Description` |
| `src/PpLint.Extractors/FlowExtractor.cs` | *(modificar)* ler `description` da ação |
| `src/PpLint.Core/Suppression/SuppressionDirective.cs` | Uma diretiva encontrada: escopo + regras |
| `src/PpLint.Core/Suppression/SuppressionIndex.cs` | Varre o IR, indexa diretivas, responde consultas |
| `src/PpLint.Core/Rules/LintContext.cs` | *(modificar)* consultar o índice em `Report` |
| `src/PpLint.Core/Rules/RuleEngine.cs` | *(modificar)* receber e repassar o índice |
| `src/PpLint.Core/Configuration/NamingPresets.cs` | Presets embutidos `camel-prefix` e `pascal-type` |
| `src/PpLint.Core/Configuration/ConfigFile.cs` | Config lido do disco, com campos opcionais |
| `src/PpLint.Core/Configuration/TomlConfigReader.cs` | TOML → `ConfigFile` (Tomlyn) |
| `src/PpLint.Core/Configuration/ConfigResolver.cs` | defaults → preset → arquivo → CLI |
| `src/PpLint.Core/Configuration/ConfigLocator.cs` | Acha `pp-lint.toml` no diretório atual e ancestrais |
| `src/PpLint.Core/PpLintConfig.cs` | *(modificar)* `Select`, `PerArtifactIgnores`, `PresetName` |
| `src/PpLint.Cli/ArgumentParser.cs` | *(modificar)* `--select`, `--ignore`, `--config` |
| `src/PpLint.Cli/Program.cs` | *(modificar)* montar config e índice, avisar sobre preset default |

---

### Task 1: Descrição da ação de fluxo

Supressão em fluxos usa o campo `description` da ação, porque JSON não aceita comentário. Hoje o extractor descarta esse campo.

**Files:**
- Modify: `src/PpLint.Core/Model/CloudFlow.cs`
- Modify: `src/PpLint.Extractors/FlowExtractor.cs`
- Test: `tests/PpLint.Extractors.Tests/FlowExtractorTests.cs` (acrescentar classe ao fim)

**Interfaces:**
- Consumes: `FlowAction` (Fase 1).
- Produces: `FlowAction.Description` — `string?`, `null` quando a ação não tem descrição.

- [ ] **Step 1: Escrever o teste que falha**

Acrescentar ao fim de `tests/PpLint.Extractors.Tests/FlowExtractorTests.cs`:

```csharp
public class FlowDescriptionTests
{
    [Fact]
    public void Extract_ReadsActionDescription()
    {
        const string json = """
        {
          "definition": {
            "actions": {
              "Compor": {
                "type": "Compose",
                "description": "pp-lint: disable=FL201",
                "inputs": "x"
              }
            }
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Equal("pp-lint: disable=FL201", Assert.Single(flow.Actions).Description);
    }

    [Fact]
    public void Extract_ActionWithoutDescriptionHasNull()
    {
        const string json = """
        { "definition": { "actions": { "Compor": { "type": "Compose", "inputs": "x" } } } }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Null(Assert.Single(flow.Actions).Description);
    }

    [Fact]
    public void Extract_DescriptionOfNestedActionIsRead()
    {
        const string json = """
        {
          "definition": {
            "actions": {
              "Loop": {
                "type": "Foreach",
                "actions": {
                  "Interno": { "type": "Compose", "description": "nota do autor", "inputs": "y" }
                }
              }
            }
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Equal("nota do autor", flow.AllActions().Single(a => a.Name == "Interno").Description);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter FlowDescriptionTests`
Expected: FALHA de compilação — `FlowAction` não tem `Description`.

- [ ] **Step 3: Adicionar a propriedade**

Em `src/PpLint.Core/Model/CloudFlow.cs`, dentro de `FlowAction`, logo abaixo de `Location`:

```csharp
    /// <summary>
    /// Descrição da ação. JSON não aceita comentário, então é aqui que vivem
    /// as diretivas de supressão do pp-lint em fluxos.
    /// </summary>
    public string? Description { get; init; }
```

- [ ] **Step 4: Ler no extractor**

Em `src/PpLint.Extractors/FlowExtractor.cs`, no método `ReadActions`, alterar a construção da ação:

```csharp
            var action = new FlowAction
            {
                Name = property.Name,
                Type = GetString(property.Value, "type") ?? string.Empty,
                Description = GetString(property.Value, "description"),
                Location = new SourceLocation(artifactPath, entryPath, property.Name, 0, 0),
            };
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Extractors.Tests`
Expected: PASSA — todos, incluindo os 3 novos.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: extrair descrição das ações de fluxo"
```

---

### Task 2: Índice de supressões

**Files:**
- Create: `src/PpLint.Core/Suppression/SuppressionDirective.cs`, `src/PpLint.Core/Suppression/SuppressionIndex.cs`
- Test: `tests/PpLint.Core.Tests/SuppressionIndexTests.cs`

**Interfaces:**
- Consumes: `PowerPlatformProject`, `CanvasApp`, `Control`, `PowerFxProperty`, `CloudFlow`, `FlowAction`, `SourceLocation`.
- Produces:
  - `sealed record SuppressionDirective(string EntryPath, string Scope, IReadOnlySet<string> RuleIds)`.
  - `SuppressionIndex` com `static SuppressionIndex Build(PowerPlatformProject project)`, `static SuppressionIndex Empty`, `bool IsSuppressed(string ruleId, SourceLocation location)`, `IReadOnlyList<SuppressionDirective> Directives`.

**Sintaxe.** `pp-lint: disable=PF111` ou `pp-lint: disable=PF111,NM011`. Em Power Fx aparece dentro de comentário (`//` ou `/* */`) — na prática basta procurar a marca em qualquer ponto do texto da fórmula, porque `pp-lint:` nunca é Power Fx válido fora de comentário. Em fluxos, aparece no campo `description`.

**Escopo.** Uma diretiva encontrada na fórmula `btnOk.OnSelect` silencia achados cujo `Symbol` seja `btnOk.OnSelect` **ou** `btnOk` — porque regras de nomenclatura reportam no controle, e regras de fórmula na propriedade. Comparação de `EntryPath` e escopo sem diferenciar maiúsculas.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/SuppressionIndexTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Suppression;

namespace PpLint.Core.Tests;

public class SuppressionIndexTests
{
    private static SourceLocation Loc(string? symbol, string entry = "Controls/1.json") =>
        new("app.msapp", entry, symbol, 0, 0);

    private static PowerPlatformProject ProjectWithFormula(string property, string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        button.Properties.Add(new PowerFxProperty(property, script, Loc($"btnOk.{property}")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "app.msapp" };
        project.Apps.Add(app);
        return project;
    }

    [Fact]
    public void FindsDirectiveInLineComment()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101\nSet(varX, 1)"));

        Assert.True(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void FindsDirectiveInBlockComment()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "/* pp-lint: disable=PF110 */ If(2 > 1, 1)"));

        Assert.True(index.IsSuppressed("PF110", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void DoesNotSuppressOtherRules()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101"));

        Assert.False(index.IsSuppressed("PF110", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void SuppressesSeveralRulesSeparatedByComma()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101,PF110"));

        Assert.True(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
        Assert.True(index.IsSuppressed("PF110", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void ToleratesSpacesAroundTheList()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable = PF101 , PF110"));

        Assert.True(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
        Assert.True(index.IsSuppressed("PF110", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void DirectiveOnPropertyAlsoSuppressesFindingsOnTheControl()
    {
        // NM011 reporta no controle; a diretiva vive numa propriedade dele.
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=NM011"));

        Assert.True(index.IsSuppressed("NM011", Loc("btnOk")));
    }

    [Fact]
    public void DoesNotLeakToAnotherControl()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101"));

        Assert.False(index.IsSuppressed("PF101", Loc("btnCancelar.OnSelect")));
    }

    [Fact]
    public void DoesNotLeakToAnotherEntry()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=PF101"));

        Assert.False(index.IsSuppressed("PF101", Loc("btnOk.OnSelect", entry: "Controls/2.json")));
    }

    [Fact]
    public void RuleIdComparisonIsCaseInsensitive()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable=pf101"));

        Assert.True(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void FindsDirectiveInFlowActionDescription()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F", "Workflows/f.json") };
        flow.Actions.Add(new FlowAction
        {
            Name = "Inicializar",
            Type = "InitializeVariable",
            Description = "temporário — pp-lint: disable=FL201",
            Location = Loc("Inicializar", "Workflows/f.json"),
        });

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        var index = SuppressionIndex.Build(project);

        Assert.True(index.IsSuppressed("FL201", Loc("Inicializar", "Workflows/f.json")));
    }

    [Fact]
    public void FormulaWithoutDirectiveProducesNoEntries()
    {
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "Set(varX, 1)"));

        Assert.Empty(index.Directives);
        Assert.False(index.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void EmptyIndexSuppressesNothing()
    {
        Assert.False(SuppressionIndex.Empty.IsSuppressed("PF101", Loc("btnOk.OnSelect")));
    }

    [Fact]
    public void MalformedDirectiveIsIgnored()
    {
        // Sem lista de regras não há o que silenciar; não pode virar "silencia tudo".
        var index = SuppressionIndex.Build(ProjectWithFormula("OnSelect", "// pp-lint: disable="));

        Assert.Empty(index.Directives);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter SuppressionIndexTests`
Expected: FALHA de compilação — o namespace `PpLint.Core.Suppression` não existe.

- [ ] **Step 3: Implementar a diretiva**

`src/PpLint.Core/Suppression/SuppressionDirective.cs`:

```csharp
namespace PpLint.Core.Suppression;

/// <summary>
/// Uma diretiva `pp-lint: disable=...` encontrada no artefato.
/// <paramref name="Scope"/> é o símbolo onde ela apareceu — a propriedade
/// (btnOk.OnSelect) ou a ação de fluxo (Inicializar).
/// </summary>
public sealed record SuppressionDirective(
    string EntryPath,
    string Scope,
    IReadOnlySet<string> RuleIds);
```

- [ ] **Step 4: Implementar o índice**

`src/PpLint.Core/Suppression/SuppressionIndex.cs`:

```csharp
using System.Text.RegularExpressions;
using PpLint.Core.Model;

namespace PpLint.Core.Suppression;

/// <summary>
/// Diretivas de supressão encontradas no artefato. Power Fx aceita comentário,
/// então a diretiva vive na própria fórmula; JSON de fluxo não aceita, então
/// vive no campo description da ação.
/// </summary>
public sealed class SuppressionIndex
{
    private static readonly Regex DirectivePattern = new(
        @"pp-lint\s*:\s*disable\s*=\s*(?<ids>[A-Za-z]{2,4}[0-9]{3}(\s*,\s*[A-Za-z]{2,4}[0-9]{3})*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly List<SuppressionDirective> _directives;

    private SuppressionIndex(List<SuppressionDirective> directives) => _directives = directives;

    public static SuppressionIndex Empty { get; } = new([]);

    public IReadOnlyList<SuppressionDirective> Directives => _directives;

    public static SuppressionIndex Build(PowerPlatformProject project)
    {
        var directives = new List<SuppressionDirective>();

        foreach (var app in project.Apps)
        {
            foreach (var property in app.AppProperties)
                AddFrom(directives, property.Script, property.Location.EntryPath, property.Location.Symbol);

            foreach (var control in app.AllControls())
                foreach (var property in control.Properties)
                    AddFrom(directives, property.Script, property.Location.EntryPath, property.Location.Symbol);
        }

        foreach (var flow in project.Flows)
            foreach (var action in flow.AllActions())
                AddFrom(directives, action.Description, action.Location.EntryPath, action.Location.Symbol);

        return new SuppressionIndex(directives);
    }

    public bool IsSuppressed(string ruleId, SourceLocation location)
    {
        foreach (var directive in _directives)
        {
            if (!string.Equals(directive.EntryPath, location.EntryPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!directive.RuleIds.Contains(ruleId))
                continue;

            if (CoversSymbol(directive.Scope, location.Symbol))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A diretiva vale para o símbolo onde está e para o seu dono: uma diretiva
    /// em btnOk.OnSelect silencia também os achados reportados em btnOk, já que
    /// regras de nomenclatura apontam para o controle e não para a propriedade.
    /// </summary>
    private static bool CoversSymbol(string scope, string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return false;

        if (string.Equals(scope, symbol, StringComparison.OrdinalIgnoreCase))
            return true;

        var dot = scope.IndexOf('.');
        return dot > 0 && string.Equals(scope[..dot], symbol, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddFrom(List<SuppressionDirective> directives, string? text, string entryPath, string? scope)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(scope))
            return;

        foreach (Match match in DirectivePattern.Matches(text))
        {
            var ids = match.Groups["ids"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (ids.Count > 0)
                directives.Add(new SuppressionDirective(entryPath, scope, ids));
        }
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Core.Tests --filter SuppressionIndexTests`
Expected: PASSA — 13 testes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: índice de diretivas de supressão"
```

---

### Task 3: Motor aplica supressão

**Files:**
- Modify: `src/PpLint.Core/Rules/LintContext.cs`
- Modify: `src/PpLint.Core/Rules/RuleEngine.cs`
- Test: `tests/PpLint.Core.Tests/SuppressionEngineTests.cs`

**Interfaces:**
- Consumes: `SuppressionIndex`, `LintContext`, `RuleEngine`, `RuleTally`.
- Produces: `RuleEngine.Run(PowerPlatformProject project, PpLintConfig config, SuppressionIndex? suppressions = null)` — a sobrecarga sem o terceiro argumento continua válida e usa `SuppressionIndex.Empty`, para não quebrar os testes da Fase 1.

**Matemática do índice.** Um achado suprimido é descontado **do numerador e do denominador**: `Report` suprimido não incrementa `ViolationCount` e decrementa `EvaluatedCount` em 1. Isso funciona porque o invariante garante no máximo uma violação por alvo — descontar a violação equivale a remover aquele alvo da conta. Sem isso, suprimir achados aumentaria a conformidade, e o número deixaria de significar algo.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/SuppressionEngineTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Core.Suppression;

namespace PpLint.Core.Tests;

[Rule("PF101", RuleCategory.PowerFx, Severity.Warning)]
public sealed class FakeUnusedVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
            foreach (var control in app.AllControls())
                foreach (var property in control.Properties)
                {
                    ctx.Evaluated(1);
                    ctx.Report(property.Location, "achado de teste");
                }
    }
}

public class SuppressionEngineTests
{
    private static SourceLocation Loc(string? symbol) => new("app.msapp", "Controls/1.json", symbol, 0, 0);

    private static PowerPlatformProject ProjectWith(params (string Property, string Script)[] properties)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        foreach (var (property, script) in properties)
            button.Properties.Add(new PowerFxProperty(property, script, Loc($"btnOk.{property}")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "app.msapp" };
        project.Apps.Add(app);
        return project;
    }

    [Fact]
    public void SuppressedFindingIsNotReported()
    {
        var project = ProjectWith(("OnSelect", "// pp-lint: disable=PF101"));
        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SuppressedFindingLeavesNumeratorAndDenominator()
    {
        var project = ProjectWith(("OnSelect", "// pp-lint: disable=PF101"));
        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(0, tally.Violations);
        Assert.Equal(0, tally.Evaluated);
    }

    [Fact]
    public void SuppressionDoesNotInflateCompliance()
    {
        // Duas propriedades, uma suprimida: a conformidade tem de ser 0%,
        // não 50%, porque o alvo suprimido some da conta inteira.
        var project = ProjectWith(
            ("OnSelect", "// pp-lint: disable=PF101"),
            ("OnChange", "Set(varX, 1)"));

        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(1, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void UnsuppressedFindingSurvives()
    {
        var project = ProjectWith(("OnSelect", "Set(varX, 1)"));
        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        Assert.Single(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void DirectiveForAnotherRuleDoesNotSuppress()
    {
        var project = ProjectWith(("OnSelect", "// pp-lint: disable=NM011"));
        var result = new RuleEngine([new FakeUnusedVariableRule()])
            .Run(project, PpLintConfig.Default, SuppressionIndex.Build(project));

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void RunWithoutIndexKeepsPhaseOneBehaviour()
    {
        var project = ProjectWith(("OnSelect", "// pp-lint: disable=PF101"));
        var result = new RuleEngine([new FakeUnusedVariableRule()]).Run(project, PpLintConfig.Default);

        Assert.Single(result.Diagnostics);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter SuppressionEngineTests`
Expected: FALHA de compilação — `Run` não aceita terceiro argumento.

- [ ] **Step 3: Ensinar supressão ao contexto**

Em `src/PpLint.Core/Rules/LintContext.cs`, acrescentar o campo, o parâmetro do construtor e a checagem em `Report`. O arquivo inteiro passa a ser:

```csharp
using PpLint.Core.Model;
using PpLint.Core.Suppression;

namespace PpLint.Core.Rules;

public sealed class LintContext
{
    private readonly string _ruleId;
    private readonly RuleCategory _category;
    private readonly Severity _severity;
    private readonly List<Diagnostic> _diagnostics;
    private readonly SuppressionIndex _suppressions;

    internal LintContext(
        string ruleId,
        RuleCategory category,
        Severity severity,
        PowerPlatformProject project,
        PpLintConfig config,
        List<Diagnostic> diagnostics,
        SuppressionIndex suppressions)
    {
        _ruleId = ruleId;
        _category = category;
        _severity = severity;
        _diagnostics = diagnostics;
        _suppressions = suppressions;
        Project = project;
        Config = config;
    }

    public PowerPlatformProject Project { get; }

    public PpLintConfig Config { get; }

    internal int EvaluatedCount { get; private set; }

    internal int ViolationCount { get; private set; }

    /// <summary>Declara quantos alvos foram examinados — o denominador do índice.</summary>
    public void Evaluated(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        EvaluatedCount += count;
    }

    public void Report(SourceLocation location, string message)
    {
        if (_suppressions.IsSuppressed(_ruleId, location))
        {
            // Suprimir remove o alvo da conta inteira. Descontar só a violação
            // faria a conformidade subir a cada supressão, e o índice deixaria
            // de significar "itens verificados que estão conformes".
            if (EvaluatedCount > 0)
                EvaluatedCount--;
            return;
        }

        _diagnostics.Add(new Diagnostic(_ruleId, _category, _severity, message, location));
        ViolationCount++;
    }
}
```

- [ ] **Step 4: Repassar o índice no motor**

Em `src/PpLint.Core/Rules/RuleEngine.cs`, alterar a assinatura de `Run` e a criação do contexto:

```csharp
    public LintResult Run(
        PowerPlatformProject project,
        PpLintConfig config,
        SuppressionIndex? suppressions = null)
    {
        var index = suppressions ?? SuppressionIndex.Empty;
        var diagnostics = new List<Diagnostic>();
        var tallies = new List<RuleTally>();

        foreach (var (rule, meta) in _rules)
        {
            if (config.Ignore.Contains(meta.Id))
                continue;

            var severity = config.SeverityOverrides.TryGetValue(meta.Id, out var overridden)
                ? overridden
                : meta.DefaultSeverity;

            var ctx = new LintContext(meta.Id, meta.Category, severity, project, config, diagnostics, index);
            var before = diagnostics.Count;

            try
            {
                rule.Check(ctx);
            }
            catch (Exception)
            {
                diagnostics.RemoveRange(before, diagnostics.Count - before);
                continue;
            }

            tallies.Add(new RuleTally(meta.Id, meta.Category, severity, ctx.EvaluatedCount, ctx.ViolationCount));
        }

        var sorted = diagnostics
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.RuleId, StringComparer.Ordinal)
            .ThenBy(d => d.Location.ToString(), StringComparer.Ordinal)
            .ToList();

        return new LintResult(sorted, tallies);
    }
```

Acrescentar `using PpLint.Core.Suppression;` ao topo do arquivo.

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — os 6 novos e todos os anteriores, sem alteração nas regras existentes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: motor respeita diretivas de supressão"
```

---

### Task 4: Presets de nomenclatura

**Files:**
- Create: `src/PpLint.Core/Configuration/NamingPresets.cs`
- Modify: `src/PpLint.Core/PpLintConfig.cs`
- Test: `tests/PpLint.Core.Tests/NamingPresetsTests.cs`

**Interfaces:**
- Consumes: `NamingConfig`.
- Produces: `NamingPresets` com `const string DefaultName = "camel-prefix"`, `static IReadOnlyList<string> Names`, `static bool TryGet(string name, out NamingConfig config)`.
- Produces: `PpLintConfig.PresetName` — `string`, default `"camel-prefix"`.

**Os dois presets.** `camel-prefix` é o preset atual (`btnSalvar`, `lblTitulo`). `pascal-type` é a convenção observada no app real validado na Fase 1 (`ButtonCreateGame`, `LabelPlayers`), em que o nome começa com o tipo em PascalCase. Ambos usam a mesma tabela de templates, mudando apenas o prefixo esperado e os regexes de variável.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/NamingPresetsTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Configuration;

namespace PpLint.Core.Tests;

public class NamingPresetsTests
{
    [Fact]
    public void DefaultPresetIsCamelPrefix()
    {
        Assert.Equal("camel-prefix", NamingPresets.DefaultName);
    }

    [Fact]
    public void CamelPrefixUsesShortPrefixes()
    {
        Assert.True(NamingPresets.TryGet("camel-prefix", out var config));
        Assert.Equal("btn", config.ControlPrefixes["button"]);
        Assert.Equal("lbl", config.ControlPrefixes["label"]);
        Assert.Equal("^var[A-Z][A-Za-z0-9]*$", config.GlobalVariable);
    }

    [Fact]
    public void PascalTypeUsesFullTypeNames()
    {
        Assert.True(NamingPresets.TryGet("pascal-type", out var config));
        Assert.Equal("Button", config.ControlPrefixes["button"]);
        Assert.Equal("Label", config.ControlPrefixes["label"]);
        Assert.Equal("Gallery", config.ControlPrefixes["gallery"]);
    }

    [Fact]
    public void PascalTypeKeepsVariableConventionInPascalCase()
    {
        Assert.True(NamingPresets.TryGet("pascal-type", out var config));
        Assert.Matches(config.GlobalVariable, "VarTotal");
        Assert.DoesNotMatch(config.GlobalVariable, "varTotal");
    }

    [Fact]
    public void BothPresetsCoverTheSameTemplates()
    {
        NamingPresets.TryGet("camel-prefix", out var camel);
        NamingPresets.TryGet("pascal-type", out var pascal);

        Assert.Equal(
            camel.ControlPrefixes.Keys.OrderBy(k => k, StringComparer.Ordinal),
            pascal.ControlPrefixes.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void UnknownPresetIsRejected()
    {
        Assert.False(NamingPresets.TryGet("inventado", out _));
    }

    [Fact]
    public void PresetLookupIsCaseInsensitive()
    {
        Assert.True(NamingPresets.TryGet("Camel-Prefix", out _));
    }

    [Fact]
    public void NamesListsEveryPreset()
    {
        Assert.Contains("camel-prefix", NamingPresets.Names);
        Assert.Contains("pascal-type", NamingPresets.Names);
    }

    [Fact]
    public void GeneratedTemplatesAreExcludedInEveryPreset()
    {
        foreach (var name in NamingPresets.Names)
        {
            NamingPresets.TryGet(name, out var config);
            Assert.Contains("galleryTemplate", config.GeneratedControlTemplates);
        }
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter NamingPresetsTests`
Expected: FALHA de compilação — o namespace `PpLint.Core.Configuration` não existe.

- [ ] **Step 3: Implementar os presets**

`src/PpLint.Core/Configuration/NamingPresets.cs`:

```csharp
namespace PpLint.Core.Configuration;

/// <summary>
/// Convenções de nomenclatura prontas. Impor uma régua única faz o linter
/// reclamar de apps bem escritos que apenas seguem outro padrão — o app real
/// usado para validar a Fase 1 gerou 129 avisos por usar PascalCase.
/// Aqui o time escolhe a régua; a lista de templates é a mesma em todos.
/// </summary>
public static class NamingPresets
{
    public const string DefaultName = "camel-prefix";

    private static readonly Dictionary<string, string> CamelPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = "btn",
        ["label"] = "lbl",
        ["text"] = "txt",
        ["textcanvas"] = "lbl",
        ["gallery"] = "gal",
        ["icon"] = "ico",
        ["image"] = "img",
        ["groupContainer"] = "cnt",
        ["form"] = "frm",
        ["dropdown"] = "drp",
        ["combobox"] = "cmb",
        ["datepicker"] = "dtp",
        ["checkbox"] = "chk",
        ["toggleSwitch"] = "tgl",
        ["radio"] = "rad",
        ["slider"] = "sld",
        ["htmlViewer"] = "htm",
        ["rectangle"] = "rec",
        ["timer"] = "tim",
    };

    private static readonly Dictionary<string, string> PascalPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = "Button",
        ["label"] = "Label",
        ["text"] = "Text",
        ["textcanvas"] = "Label",
        ["gallery"] = "Gallery",
        ["icon"] = "Icon",
        ["image"] = "Image",
        ["groupContainer"] = "Container",
        ["form"] = "Form",
        ["dropdown"] = "Dropdown",
        ["combobox"] = "Combo",
        ["datepicker"] = "Date",
        ["checkbox"] = "Check",
        ["toggleSwitch"] = "Toggle",
        ["radio"] = "Radio",
        ["slider"] = "Slider",
        ["htmlViewer"] = "Html",
        ["rectangle"] = "Rect",
        ["timer"] = "Timer",
    };

    public static IReadOnlyList<string> Names { get; } = ["camel-prefix", "pascal-type"];

    public static bool TryGet(string name, out NamingConfig config)
    {
        if (string.Equals(name, "camel-prefix", StringComparison.OrdinalIgnoreCase))
        {
            config = new NamingConfig
            {
                GlobalVariable = "^var[A-Z][A-Za-z0-9]*$",
                ContextVariable = "^loc[A-Z][A-Za-z0-9]*$",
                Collection = "^col[A-Z][A-Za-z0-9]*$",
                Screen = "^scr[A-Z][A-Za-z0-9]*$",
                Component = "^cmp[A-Z][A-Za-z0-9]*$",
                ControlPrefixes = CamelPrefixes,
            };
            return true;
        }

        if (string.Equals(name, "pascal-type", StringComparison.OrdinalIgnoreCase))
        {
            config = new NamingConfig
            {
                GlobalVariable = "^Var[A-Z][A-Za-z0-9]*$",
                ContextVariable = "^Loc[A-Z][A-Za-z0-9]*$",
                Collection = "^Col[A-Z][A-Za-z0-9]*$",
                Screen = "^Screen[A-Z][A-Za-z0-9]*$",
                Component = "^Component[A-Z][A-Za-z0-9]*$",
                ControlPrefixes = PascalPrefixes,
            };
            return true;
        }

        config = new NamingConfig();
        return false;
    }
}
```

- [ ] **Step 4: Guardar o preset escolhido na config**

Em `src/PpLint.Core/PpLintConfig.cs`, dentro de `PpLintConfig`, acrescentar antes de `Naming`:

```csharp
    /// <summary>Nome do preset de nomenclatura em vigor, para exibição e diagnóstico.</summary>
    public string PresetName { get; init; } = "camel-prefix";
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Core.Tests --filter NamingPresetsTests`
Expected: PASSA — 9 testes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: presets de nomenclatura camel-prefix e pascal-type"
```

---

### Task 5: Leitura do arquivo TOML

**Files:**
- Create: `src/PpLint.Core/Configuration/ConfigFile.cs`, `src/PpLint.Core/Configuration/TomlConfigReader.cs`
- Modify: `src/PpLint.Core/PpLint.Core.csproj` (pacote Tomlyn)
- Test: `tests/PpLint.Core.Tests/TomlConfigReaderTests.cs`

**Interfaces:**
- Consumes: `Severity`.
- Produces:
  - `sealed record ConfigFile` com `string? Preset`, `IReadOnlyList<string>? Select`, `IReadOnlyList<string>? Ignore`, `IReadOnlyDictionary<string, Severity>? SeverityOverrides`, `IReadOnlyDictionary<string, string>? ControlPrefixes`, `IReadOnlyDictionary<string, string>? NamingPatterns`, `IReadOnlyDictionary<string, IReadOnlyList<string>>? PerArtifactIgnores`, `Severity? FailOn`.
  - `static ConfigFile TomlConfigReader.Read(string toml)` — lança `ConfigException` para TOML inválido ou valor inesperado.
  - `sealed class ConfigException : Exception`.

Campo ausente vira `null`, nunca um default silencioso: quem decide o default é o `ConfigResolver` da Task 6. Essa separação é o que permite distinguir "o usuário não disse nada" de "o usuário pediu exatamente o valor default".

- [ ] **Step 1: Instalar o Tomlyn e verificar a API**

```bash
cd /c/PROJETOS/pp-lint
dotnet add src/PpLint.Core package Tomlyn
```

Criar `tests/PpLint.Core.Tests/TomlSpike.cs`, rodar, ler a saída e **apagar antes do commit**:

```csharp
using Tomlyn;
using Tomlyn.Model;
using Xunit.Abstractions;

namespace PpLint.Core.Tests;

public class TomlSpike(ITestOutputHelper output)
{
    [Fact]
    public void PrintModelShape()
    {
        const string toml = """
            [pp-lint]
            preset = "pascal-type"
            ignore = ["PF125", "FL240"]

            [pp-lint.naming.control-prefixes]
            button = "btn"
            """;

        var model = Toml.ToModel(toml);
        output.WriteLine($"raiz: {model.GetType().FullName}");

        var section = (TomlTable)model["pp-lint"]!;
        output.WriteLine($"secao: {section.GetType().FullName}");
        output.WriteLine($"preset: {section["preset"]} ({section["preset"]!.GetType().Name})");
        output.WriteLine($"ignore: {section["ignore"]!.GetType().FullName}");

        var naming = (TomlTable)section["naming"]!;
        var prefixes = (TomlTable)naming["control-prefixes"]!;
        output.WriteLine($"prefixo button: {prefixes["button"]}");
    }
}
```

Run: `dotnet test tests/PpLint.Core.Tests --filter PrintModelShape --logger "console;verbosity=detailed"`

Confirme antes de seguir: `Toml.ToModel` devolve um `TomlTable` indexável por string; seções aninhadas são `TomlTable`; arrays são `TomlArray`. Se algum nome divergir, ajuste o leitor mantendo a assinatura de `TomlConfigReader.Read` intacta.

- [ ] **Step 2: Escrever o teste que falha**

`tests/PpLint.Core.Tests/TomlConfigReaderTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Configuration;

namespace PpLint.Core.Tests;

public class TomlConfigReaderTests
{
    [Fact]
    public void ReadsPreset()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            preset = "pascal-type"
            """);

        Assert.Equal("pascal-type", config.Preset);
    }

    [Fact]
    public void ReadsSelectAndIgnoreLists()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            select = ["NM", "PF101"]
            ignore = ["PF125"]
            """);

        Assert.Equal(["NM", "PF101"], config.Select);
        Assert.Equal(["PF125"], config.Ignore);
    }

    [Fact]
    public void ReadsFailOn()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            fail-on = "warning"
            """);

        Assert.Equal(Severity.Warning, config.FailOn);
    }

    [Fact]
    public void ReadsSeverityOverrides()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint.severity-overrides]
            NM011 = "info"
            PF101 = "error"
            """);

        Assert.Equal(Severity.Info, config.SeverityOverrides!["NM011"]);
        Assert.Equal(Severity.Error, config.SeverityOverrides["PF101"]);
    }

    [Fact]
    public void ReadsControlPrefixes()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint.naming.control-prefixes]
            button = "bt"
            label = "lb"
            """);

        Assert.Equal("bt", config.ControlPrefixes!["button"]);
        Assert.Equal("lb", config.ControlPrefixes["label"]);
    }

    [Fact]
    public void ReadsNamingPatterns()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint.naming]
            global-variable = "^g[A-Z].*$"
            screen = "^s[A-Z].*$"
            """);

        Assert.Equal("^g[A-Z].*$", config.NamingPatterns!["global-variable"]);
        Assert.Equal("^s[A-Z].*$", config.NamingPatterns["screen"]);
    }

    [Fact]
    public void ReadsPerArtifactIgnores()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint.per-artifact-ignores]
            "**/Legado*.msapp" = ["NM010", "NM011"]
            """);

        Assert.Equal(["NM010", "NM011"], config.PerArtifactIgnores!["**/Legado*.msapp"]);
    }

    [Fact]
    public void AbsentFieldsStayNull()
    {
        var config = TomlConfigReader.Read("""
            [pp-lint]
            preset = "camel-prefix"
            """);

        Assert.Null(config.Ignore);
        Assert.Null(config.FailOn);
        Assert.Null(config.ControlPrefixes);
        Assert.Null(config.PerArtifactIgnores);
    }

    [Fact]
    public void EmptyFileIsValidAndEmpty()
    {
        var config = TomlConfigReader.Read("");

        Assert.Null(config.Preset);
        Assert.Null(config.Select);
    }

    [Fact]
    public void MalformedTomlThrowsConfigException()
    {
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("[pp-lint\npreset ="));
        Assert.Contains("TOML", ex.Message);
    }

    [Fact]
    public void UnknownSeverityThrowsConfigException()
    {
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("""
            [pp-lint]
            fail-on = "critico"
            """));

        Assert.Contains("critico", ex.Message);
    }

    [Fact]
    public void WrongTypeThrowsConfigExceptionNamingTheField()
    {
        var ex = Assert.Throws<ConfigException>(() => TomlConfigReader.Read("""
            [pp-lint]
            ignore = "PF125"
            """));

        Assert.Contains("ignore", ex.Message);
    }
}
```

- [ ] **Step 3: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter TomlConfigReaderTests`
Expected: FALHA de compilação — `TomlConfigReader` não existe.

- [ ] **Step 4: Implementar o modelo do arquivo**

`src/PpLint.Core/Configuration/ConfigFile.cs`:

```csharp
namespace PpLint.Core.Configuration;

/// <summary>
/// O que o arquivo de configuração disse — nada mais. Campo ausente é null,
/// nunca um default: só assim o resolver distingue "o usuário não falou nada"
/// de "o usuário pediu exatamente o valor default".
/// </summary>
public sealed record ConfigFile
{
    public string? Preset { get; init; }
    public IReadOnlyList<string>? Select { get; init; }
    public IReadOnlyList<string>? Ignore { get; init; }
    public Severity? FailOn { get; init; }
    public IReadOnlyDictionary<string, Severity>? SeverityOverrides { get; init; }
    public IReadOnlyDictionary<string, string>? ControlPrefixes { get; init; }
    public IReadOnlyDictionary<string, string>? NamingPatterns { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? PerArtifactIgnores { get; init; }

    public static ConfigFile Empty { get; } = new();
}

public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
    public ConfigException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 5: Implementar o leitor**

`src/PpLint.Core/Configuration/TomlConfigReader.cs`:

```csharp
using Tomlyn;
using Tomlyn.Model;

namespace PpLint.Core.Configuration;

/// <summary>
/// Lê pp-lint.toml. Erros são sempre explícitos: configuração ignorada em
/// silêncio faz o time acreditar que configurou algo que não vale.
/// </summary>
public static class TomlConfigReader
{
    public static ConfigFile Read(string toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
            return ConfigFile.Empty;

        TomlTable root;
        try
        {
            root = Toml.ToModel(toml);
        }
        catch (Exception ex)
        {
            throw new ConfigException($"TOML inválido: {ex.Message}", ex);
        }

        if (root.TryGetValue("pp-lint", out var raw) && raw is not TomlTable)
            throw new ConfigException("A seção [pp-lint] precisa ser uma tabela.");

        var section = raw as TomlTable ?? new TomlTable();
        var naming = GetTable(section, "naming");

        return new ConfigFile
        {
            Preset = GetString(section, "preset"),
            Select = GetStringList(section, "select"),
            Ignore = GetStringList(section, "ignore"),
            FailOn = GetSeverity(section, "fail-on"),
            SeverityOverrides = GetSeverityMap(GetTable(section, "severity-overrides")),
            ControlPrefixes = GetStringMap(GetTable(naming, "control-prefixes")),
            NamingPatterns = GetNamingPatterns(naming),
            PerArtifactIgnores = GetListMap(GetTable(section, "per-artifact-ignores")),
        };
    }

    private static TomlTable? GetTable(TomlTable? parent, string key)
    {
        if (parent is null || !parent.TryGetValue(key, out var value))
            return null;

        return value as TomlTable
               ?? throw new ConfigException($"'{key}' precisa ser uma tabela.");
    }

    private static string? GetString(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
            return null;

        return value as string
               ?? throw new ConfigException($"'{key}' precisa ser um texto.");
    }

    private static IReadOnlyList<string>? GetStringList(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
            return null;

        if (value is not TomlArray array)
            throw new ConfigException($"'{key}' precisa ser uma lista, por exemplo ignore = [\"PF125\"].");

        return array.Select(item =>
            item as string ?? throw new ConfigException($"'{key}' só aceita textos.")).ToList();
    }

    private static Severity? GetSeverity(TomlTable? table, string key)
    {
        var text = GetString(table, key);
        return text is null ? null : ParseSeverity(text, key);
    }

    private static Severity ParseSeverity(string text, string context) => text.ToLowerInvariant() switch
    {
        "error" => Severity.Error,
        "warning" => Severity.Warning,
        "info" => Severity.Info,
        _ => throw new ConfigException(
            $"Severidade inválida em '{context}': '{text}'. Válidas: error, warning, info."),
    };

    private static IReadOnlyDictionary<string, Severity>? GetSeverityMap(TomlTable? table)
    {
        if (table is null)
            return null;

        var result = new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in table)
        {
            var text = value as string
                       ?? throw new ConfigException($"A severidade de '{key}' precisa ser um texto.");
            result[key] = ParseSeverity(text, key);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string>? GetStringMap(TomlTable? table)
    {
        if (table is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in table)
        {
            result[key] = value as string
                          ?? throw new ConfigException($"O valor de '{key}' precisa ser um texto.");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? GetListMap(TomlTable? table)
    {
        if (table is null)
            return null;

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in table)
        {
            if (value is not TomlArray array)
                throw new ConfigException($"'{key}' precisa ser uma lista de IDs de regra.");

            result[key] = array.Select(item =>
                item as string ?? throw new ConfigException($"'{key}' só aceita textos.")).ToList();
        }

        return result;
    }

    /// <summary>
    /// Chaves de nomenclatura que não são a tabela de prefixos: global-variable,
    /// context-variable, collection, screen, component.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? GetNamingPatterns(TomlTable? naming)
    {
        if (naming is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in naming)
        {
            if (value is TomlTable)
                continue;

            result[key] = value as string
                          ?? throw new ConfigException($"O padrão de '{key}' precisa ser um texto.");
        }

        return result.Count > 0 ? result : null;
    }
}
```

- [ ] **Step 6: Rodar, apagar o spike e commitar**

```bash
cd /c/PROJETOS/pp-lint
dotnet test tests/PpLint.Core.Tests --filter TomlConfigReaderTests
rm tests/PpLint.Core.Tests/TomlSpike.cs
dotnet test
git add -A
git commit -m "feat: leitura de pp-lint.toml"
```

Expected: 12 testes de `TomlConfigReaderTests` passando, suíte inteira verde.

---

### Task 6: Resolver de configuração

**Files:**
- Create: `src/PpLint.Core/Configuration/ConfigResolver.cs`, `src/PpLint.Core/Configuration/CliOverrides.cs`
- Modify: `src/PpLint.Core/PpLintConfig.cs` (campos `Select` e `PerArtifactIgnores`)
- Test: `tests/PpLint.Core.Tests/ConfigResolverTests.cs`

**Interfaces:**
- Consumes: `ConfigFile`, `NamingPresets`, `PpLintConfig`, `NamingConfig`, `Severity`.
- Produces:
  - `sealed record CliOverrides(IReadOnlyList<string>? Select, IReadOnlyList<string>? Ignore, Severity? FailOn)` com `static CliOverrides None`.
  - `static PpLintConfig ConfigResolver.Resolve(ConfigFile file, CliOverrides cli)`.
- Produces: `PpLintConfig.Select` — `IReadOnlySet<string>`, vazio significa "todas as regras"; `PpLintConfig.PerArtifactIgnores` — `IReadOnlyDictionary<string, IReadOnlyList<string>>`; `PpLintConfig.FailOn` — `Severity`, default `Error`.

**Precedência.** defaults do preset → padrões e prefixos do arquivo → flags de CLI. Listas de CLI **substituem** as do arquivo, não somam: quem passa `--ignore` na linha de comando está dizendo exatamente o que quer ignorar naquela execução.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/ConfigResolverTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Configuration;

namespace PpLint.Core.Tests;

public class ConfigResolverTests
{
    [Fact]
    public void WithoutAnythingUsesDefaultPreset()
    {
        var config = ConfigResolver.Resolve(ConfigFile.Empty, CliOverrides.None);

        Assert.Equal("camel-prefix", config.PresetName);
        Assert.Equal("btn", config.Naming.ControlPrefixes["button"]);
        Assert.Equal(Severity.Error, config.FailOn);
        Assert.Empty(config.Select);
        Assert.Empty(config.Ignore);
    }

    [Fact]
    public void PresetFromFileChangesPrefixes()
    {
        var config = ConfigResolver.Resolve(new ConfigFile { Preset = "pascal-type" }, CliOverrides.None);

        Assert.Equal("pascal-type", config.PresetName);
        Assert.Equal("Button", config.Naming.ControlPrefixes["button"]);
    }

    [Fact]
    public void UnknownPresetThrowsConfigExceptionListingValidOnes()
    {
        var ex = Assert.Throws<ConfigException>(() =>
            ConfigResolver.Resolve(new ConfigFile { Preset = "inventado" }, CliOverrides.None));

        Assert.Contains("inventado", ex.Message);
        Assert.Contains("camel-prefix", ex.Message);
    }

    [Fact]
    public void FilePrefixesOverrideOnlyWhatTheyMention()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile
            {
                ControlPrefixes = new Dictionary<string, string> { ["button"] = "bt" },
            },
            CliOverrides.None);

        Assert.Equal("bt", config.Naming.ControlPrefixes["button"]);
        Assert.Equal("lbl", config.Naming.ControlPrefixes["label"]); // veio do preset
    }

    [Fact]
    public void FileNamingPatternsOverridePreset()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile
            {
                NamingPatterns = new Dictionary<string, string>
                {
                    ["global-variable"] = "^g[A-Z].*$",
                    ["screen"] = "^s[A-Z].*$",
                },
            },
            CliOverrides.None);

        Assert.Equal("^g[A-Z].*$", config.Naming.GlobalVariable);
        Assert.Equal("^s[A-Z].*$", config.Naming.Screen);
        Assert.Equal("^loc[A-Z][A-Za-z0-9]*$", config.Naming.ContextVariable); // intocado
    }

    [Fact]
    public void UnknownNamingPatternKeyThrows()
    {
        var ex = Assert.Throws<ConfigException>(() => ConfigResolver.Resolve(
            new ConfigFile { NamingPatterns = new Dictionary<string, string> { ["cor-do-botao"] = "x" } },
            CliOverrides.None));

        Assert.Contains("cor-do-botao", ex.Message);
    }

    [Fact]
    public void CliIgnoreReplacesFileIgnore()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile { Ignore = ["PF101"] },
            new CliOverrides(Select: null, Ignore: ["NM011"], FailOn: null));

        Assert.Contains("NM011", config.Ignore);
        Assert.DoesNotContain("PF101", config.Ignore);
    }

    [Fact]
    public void CliFailOnWins()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile { FailOn = Severity.Error },
            new CliOverrides(null, null, Severity.Info));

        Assert.Equal(Severity.Info, config.FailOn);
    }

    [Fact]
    public void FileFailOnUsedWhenCliSaysNothing()
    {
        var config = ConfigResolver.Resolve(new ConfigFile { FailOn = Severity.Warning }, CliOverrides.None);

        Assert.Equal(Severity.Warning, config.FailOn);
    }

    [Fact]
    public void SeverityOverridesComeFromFile()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile
            {
                SeverityOverrides = new Dictionary<string, Severity> { ["NM011"] = Severity.Info },
            },
            CliOverrides.None);

        Assert.Equal(Severity.Info, config.SeverityOverrides["NM011"]);
    }

    [Fact]
    public void PerArtifactIgnoresAreCarried()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile
            {
                PerArtifactIgnores = new Dictionary<string, IReadOnlyList<string>>
                {
                    ["**/Legado*.msapp"] = ["NM010"],
                },
            },
            CliOverrides.None);

        Assert.Equal(["NM010"], config.PerArtifactIgnores["**/Legado*.msapp"]);
    }

    [Fact]
    public void SelectFromCliWins()
    {
        var config = ConfigResolver.Resolve(
            new ConfigFile { Select = ["NM"] },
            new CliOverrides(Select: ["PF"], Ignore: null, FailOn: null));

        Assert.Contains("PF", config.Select);
        Assert.DoesNotContain("NM", config.Select);
    }

    [Fact]
    public void ResolveIsPure()
    {
        var file = new ConfigFile { Preset = "pascal-type" };

        var first = ConfigResolver.Resolve(file, CliOverrides.None);
        var second = ConfigResolver.Resolve(file, CliOverrides.None);

        Assert.Equal(first.PresetName, second.PresetName);
        Assert.Equal(first.Naming.ControlPrefixes["button"], second.Naming.ControlPrefixes["button"]);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter ConfigResolverTests`
Expected: FALHA de compilação — `ConfigResolver` não existe.

- [ ] **Step 3: Ampliar o PpLintConfig**

Em `src/PpLint.Core/PpLintConfig.cs`, dentro de `PpLintConfig`, acrescentar:

```csharp
    /// <summary>Regras a executar; vazio significa todas. Aceita ID (PF101) ou prefixo (PF).</summary>
    public IReadOnlySet<string> Select { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Glob do artefato para as regras que ele não deve receber.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PerArtifactIgnores { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>Severidade a partir da qual o processo termina com código 1.</summary>
    public Severity FailOn { get; init; } = Severity.Error;
```

- [ ] **Step 4: Implementar os overrides de CLI**

`src/PpLint.Core/Configuration/CliOverrides.cs`:

```csharp
namespace PpLint.Core.Configuration;

/// <summary>
/// O que a linha de comando pediu. Null significa "não falei nada" — o valor
/// do arquivo permanece.
/// </summary>
public sealed record CliOverrides(
    IReadOnlyList<string>? Select,
    IReadOnlyList<string>? Ignore,
    Severity? FailOn)
{
    public static CliOverrides None { get; } = new(null, null, null);
}
```

- [ ] **Step 5: Implementar o resolver**

`src/PpLint.Core/Configuration/ConfigResolver.cs`:

```csharp
namespace PpLint.Core.Configuration;

/// <summary>
/// Junta preset, arquivo e linha de comando num único config imutável.
/// Precedência: preset → arquivo → CLI. Listas de CLI substituem as do
/// arquivo em vez de somar: quem passa --ignore está dizendo exatamente o
/// que quer ignorar naquela execução.
/// </summary>
public static class ConfigResolver
{
    public static PpLintConfig Resolve(ConfigFile file, CliOverrides cli)
    {
        var presetName = file.Preset ?? NamingPresets.DefaultName;

        if (!NamingPresets.TryGet(presetName, out var naming))
            throw new ConfigException(
                $"Preset desconhecido: '{presetName}'. Disponíveis: {string.Join(", ", NamingPresets.Names)}.");

        naming = ApplyNamingPatterns(naming, file.NamingPatterns);
        naming = ApplyControlPrefixes(naming, file.ControlPrefixes);

        return new PpLintConfig
        {
            PresetName = presetName,
            Naming = naming,
            Select = ToSet(cli.Select ?? file.Select),
            Ignore = ToSet(cli.Ignore ?? file.Ignore),
            FailOn = cli.FailOn ?? file.FailOn ?? Severity.Error,
            SeverityOverrides = file.SeverityOverrides
                                ?? new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase),
            PerArtifactIgnores = file.PerArtifactIgnores
                                 ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        };
    }

    private static NamingConfig ApplyNamingPatterns(
        NamingConfig naming, IReadOnlyDictionary<string, string>? patterns)
    {
        if (patterns is null)
            return naming;

        foreach (var (key, value) in patterns)
        {
            naming = key.ToLowerInvariant() switch
            {
                "global-variable" => naming with { GlobalVariable = value },
                "context-variable" => naming with { ContextVariable = value },
                "collection" => naming with { Collection = value },
                "screen" => naming with { Screen = value },
                "component" => naming with { Component = value },
                _ => throw new ConfigException(
                    $"Chave de nomenclatura desconhecida: '{key}'. Válidas: global-variable, "
                    + "context-variable, collection, screen, component."),
            };
        }

        return naming;
    }

    private static NamingConfig ApplyControlPrefixes(
        NamingConfig naming, IReadOnlyDictionary<string, string>? prefixes)
    {
        if (prefixes is null)
            return naming;

        var merged = new Dictionary<string, string>(naming.ControlPrefixes, StringComparer.OrdinalIgnoreCase);
        foreach (var (template, prefix) in prefixes)
            merged[template] = prefix;

        return naming with { ControlPrefixes = merged };
    }

    private static IReadOnlySet<string> ToSet(IReadOnlyList<string>? values) =>
        values is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 6: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Core.Tests --filter ConfigResolverTests`
Expected: PASSA — 13 testes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: resolver de configuração com precedência preset/arquivo/CLI"
```

---

### Task 7: Seleção de regras e ignores por artefato

**Files:**
- Create: `src/PpLint.Core/Rules/RuleFilter.cs`
- Modify: `src/PpLint.Core/Rules/RuleEngine.cs`
- Test: `tests/PpLint.Core.Tests/RuleFilterTests.cs`

**Interfaces:**
- Consumes: `PpLintConfig`, `RuleAttribute`.
- Produces: `static class RuleFilter` com `static bool ShouldRun(string ruleId, PpLintConfig config)` e `static bool IsIgnoredForArtifact(string ruleId, string artifactPath, PpLintConfig config)`.

**Semântica.** `Select` vazio significa todas. Um item de `Select`/`Ignore` casa com o ID exato (`PF101`) ou com o prefixo de categoria (`PF`). `Ignore` vence `Select`. O glob de `per-artifact-ignores` suporta `*` (qualquer trecho sem separador) e `**` (qualquer trecho, inclusive separadores) e é comparado contra o caminho do artefato com `/` normalizado.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/RuleFilterTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Core.Tests;

public class RuleFilterTests
{
    private static PpLintConfig With(string[]? select = null, string[]? ignore = null) =>
        PpLintConfig.Default with
        {
            Select = new HashSet<string>(select ?? [], StringComparer.OrdinalIgnoreCase),
            Ignore = new HashSet<string>(ignore ?? [], StringComparer.OrdinalIgnoreCase),
        };

    [Fact]
    public void EmptySelectRunsEverything()
    {
        Assert.True(RuleFilter.ShouldRun("PF101", With()));
        Assert.True(RuleFilter.ShouldRun("NM010", With()));
    }

    [Fact]
    public void SelectByExactId()
    {
        var config = With(select: ["PF101"]);

        Assert.True(RuleFilter.ShouldRun("PF101", config));
        Assert.False(RuleFilter.ShouldRun("PF110", config));
    }

    [Fact]
    public void SelectByCategoryPrefix()
    {
        var config = With(select: ["NM"]);

        Assert.True(RuleFilter.ShouldRun("NM010", config));
        Assert.True(RuleFilter.ShouldRun("NM011", config));
        Assert.False(RuleFilter.ShouldRun("PF101", config));
    }

    [Fact]
    public void IgnoreBeatsSelect()
    {
        var config = With(select: ["NM"], ignore: ["NM011"]);

        Assert.True(RuleFilter.ShouldRun("NM010", config));
        Assert.False(RuleFilter.ShouldRun("NM011", config));
    }

    [Fact]
    public void IgnoreByCategoryPrefix()
    {
        Assert.False(RuleFilter.ShouldRun("FL201", With(ignore: ["FL"])));
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        Assert.True(RuleFilter.ShouldRun("PF101", With(select: ["pf101"])));
    }

    [Fact]
    public void PrefixDoesNotMatchAcrossCategories()
    {
        // "P" não deve seleccionar PF101 por acidente: o prefixo é a parte alfabética inteira.
        Assert.False(RuleFilter.ShouldRun("PF101", With(select: ["P"])));
    }

    [Fact]
    public void PerArtifactIgnoreMatchesGlob()
    {
        var config = PpLintConfig.Default with
        {
            PerArtifactIgnores = new Dictionary<string, IReadOnlyList<string>>
            {
                ["**/Legado*.msapp"] = ["NM010"],
            },
        };

        Assert.True(RuleFilter.IsIgnoredForArtifact("NM010", "apps/LegadoVendas.msapp", config));
        Assert.False(RuleFilter.IsIgnoredForArtifact("NM011", "apps/LegadoVendas.msapp", config));
        Assert.False(RuleFilter.IsIgnoredForArtifact("NM010", "apps/NovoVendas.msapp", config));
    }

    [Fact]
    public void PerArtifactIgnoreAcceptsCategoryPrefix()
    {
        var config = PpLintConfig.Default with
        {
            PerArtifactIgnores = new Dictionary<string, IReadOnlyList<string>>
            {
                ["*.msapp"] = ["NM"],
            },
        };

        Assert.True(RuleFilter.IsIgnoredForArtifact("NM010", "App.msapp", config));
        Assert.False(RuleFilter.IsIgnoredForArtifact("PF101", "App.msapp", config));
    }

    [Fact]
    public void PerArtifactIgnoreNormalizesBackslashes()
    {
        var config = PpLintConfig.Default with
        {
            PerArtifactIgnores = new Dictionary<string, IReadOnlyList<string>>
            {
                ["**/apps/*.msapp"] = ["NM010"],
            },
        };

        Assert.True(RuleFilter.IsIgnoredForArtifact("NM010", @"C:\repo\apps\Vendas.msapp", config));
    }

    [Fact]
    public void WithoutPerArtifactIgnoresNothingIsIgnored()
    {
        Assert.False(RuleFilter.IsIgnoredForArtifact("NM010", "App.msapp", PpLintConfig.Default));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter RuleFilterTests`
Expected: FALHA de compilação — `RuleFilter` não existe.

- [ ] **Step 3: Implementar o filtro**

`src/PpLint.Core/Rules/RuleFilter.cs`:

```csharp
using System.Text.RegularExpressions;

namespace PpLint.Core.Rules;

/// <summary>
/// Decide quais regras rodam. Um item de select/ignore casa com o ID inteiro
/// (PF101) ou com a categoria (PF); ignore sempre vence select.
/// </summary>
public static class RuleFilter
{
    public static bool ShouldRun(string ruleId, PpLintConfig config)
    {
        if (Matches(ruleId, config.Ignore))
            return false;

        return config.Select.Count == 0 || Matches(ruleId, config.Select);
    }

    public static bool IsIgnoredForArtifact(string ruleId, string artifactPath, PpLintConfig config)
    {
        if (config.PerArtifactIgnores.Count == 0)
            return false;

        var normalized = artifactPath.Replace('\\', '/');

        foreach (var (glob, ruleIds) in config.PerArtifactIgnores)
        {
            if (!GlobMatches(glob, normalized))
                continue;

            if (Matches(ruleId, new HashSet<string>(ruleIds, StringComparer.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    /// <summary>O ID casa com ele mesmo ou com sua categoria — a parte alfabética inicial.</summary>
    private static bool Matches(string ruleId, IReadOnlySet<string> patterns)
    {
        if (patterns.Contains(ruleId))
            return true;

        var category = new string(ruleId.TakeWhile(char.IsAsciiLetter).ToArray());
        return category.Length > 0 && patterns.Contains(category);
    }

    private static bool GlobMatches(string glob, string path)
    {
        var pattern = "^" + Regex.Escape(glob)
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", ".") + "$";

        return Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
```

- [ ] **Step 4: Usar o filtro no motor**

Em `src/PpLint.Core/Rules/RuleEngine.cs`, substituir a linha de descarte por ignore:

```csharp
            if (!RuleFilter.ShouldRun(meta.Id, config))
                continue;

            if (RuleFilter.IsIgnoredForArtifact(meta.Id, project.SourcePath, config))
                continue;
```

no lugar de:

```csharp
            if (config.Ignore.Contains(meta.Id))
                continue;
```

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — inclusive `RuleEngineTests.Run_IgnoredRuleDoesNotRun`, da Fase 1, que continua válido porque `Ignore` segue sendo respeitado.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: seleção de regras por ID ou categoria e ignores por artefato"
```

---

### Task 8: Descoberta do arquivo de configuração

**Files:**
- Create: `src/PpLint.Core/Configuration/ConfigLocator.cs`
- Test: `tests/PpLint.Core.Tests/ConfigLocatorTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `static class ConfigLocator` com `const string FileName = "pp-lint.toml"` e `static string? Find(string startDirectory)`.

Procura no diretório informado e sobe pelos ancestrais até a raiz, devolvendo o primeiro achado — o mesmo comportamento do ruff, do ESLint e do EditorConfig, familiar para quem já usa qualquer um deles.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/ConfigLocatorTests.cs`:

```csharp
using PpLint.Core.Configuration;

namespace PpLint.Core.Tests;

public class ConfigLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pplint-cfg-{Guid.NewGuid():N}");

    public ConfigLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FindsFileInTheStartDirectory()
    {
        var file = Path.Combine(_root, "pp-lint.toml");
        File.WriteAllText(file, "[pp-lint]");

        Assert.Equal(file, ConfigLocator.Find(_root));
    }

    [Fact]
    public void FindsFileInAnAncestor()
    {
        var file = Path.Combine(_root, "pp-lint.toml");
        File.WriteAllText(file, "[pp-lint]");

        var deep = Path.Combine(_root, "src", "apps");
        Directory.CreateDirectory(deep);

        Assert.Equal(file, ConfigLocator.Find(deep));
    }

    [Fact]
    public void NearestAncestorWins()
    {
        File.WriteAllText(Path.Combine(_root, "pp-lint.toml"), "[pp-lint]");

        var nested = Path.Combine(_root, "src");
        Directory.CreateDirectory(nested);
        var nearer = Path.Combine(nested, "pp-lint.toml");
        File.WriteAllText(nearer, "[pp-lint]");

        Assert.Equal(nearer, ConfigLocator.Find(nested));
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoFile()
    {
        var empty = Path.Combine(_root, "vazio");
        Directory.CreateDirectory(empty);

        // Pode existir um pp-lint.toml acima do temp em teoria; o teste usa um
        // diretório recém-criado sob o temp, onde isso não acontece na prática.
        Assert.Null(ConfigLocator.Find(empty));
    }

    [Fact]
    public void MissingDirectoryReturnsNull()
    {
        Assert.Null(ConfigLocator.Find(Path.Combine(_root, "nao-existe")));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter ConfigLocatorTests`
Expected: FALHA de compilação — `ConfigLocator` não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.Core/Configuration/ConfigLocator.cs`:

```csharp
namespace PpLint.Core.Configuration;

/// <summary>
/// Acha o pp-lint.toml subindo do diretório atual até a raiz, como fazem
/// ruff, ESLint e EditorConfig.
/// </summary>
public static class ConfigLocator
{
    public const string FileName = "pp-lint.toml";

    public static string? Find(string startDirectory)
    {
        if (!Directory.Exists(startDirectory))
            return null;

        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, FileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return null;
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Core.Tests --filter ConfigLocatorTests`
Expected: PASSA — 5 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: descoberta do pp-lint.toml no diretório atual e ancestrais"
```

---

### Task 9: Integração no CLI

**Files:**
- Modify: `src/PpLint.Cli/ArgumentParser.cs`
- Modify: `src/PpLint.Cli/Program.cs`
- Test: `tests/PpLint.Cli.Tests/ArgumentParserTests.cs` (acrescentar classe ao fim)
- Test: `tests/PpLint.Cli.Tests/ConfigIntegrationTests.cs`

**Interfaces:**
- Consumes: `ConfigLocator`, `TomlConfigReader`, `ConfigResolver`, `CliOverrides`, `ConfigException`, `SuppressionIndex`, `NamingPresets`.
- Produces: `CliOptions` ganha `IReadOnlyList<string> Select`, `IReadOnlyList<string> Ignore`, `string? ConfigPath`. `CliOptions.FailOn` passa a ser `Severity?` — `null` significa "não foi passado", deixando o arquivo decidir.

**Aviso de preset.** Quando não há `pp-lint.toml` e existe ao menos um achado de nomenclatura, o `check` imprime uma linha ao fim explicando qual preset está em uso e como trocar. Uma vez por execução, em `stderr`, para não poluir a saída consumida por ferramentas.

> **Cuidado com o próprio repositório.** `WarnsAboutDefaultPresetWhenThereIsNoConfig` depende de não existir `pp-lint.toml` em nenhum ancestral do diretório de trabalho dos testes — que fica dentro deste repositório. Por isso o arquivo de exemplo da Task 10 se chama `pp-lint.example.toml`: criar um `pp-lint.toml` de verdade na raiz faria esse teste falhar sem que nada tivesse quebrado no produto. Se algum dia o projeto precisar de configuração própria, esse teste passa a exigir isolamento explícito do diretório de trabalho.

- [ ] **Step 1: Escrever o teste do parser**

Acrescentar ao fim de `tests/PpLint.Cli.Tests/ArgumentParserTests.cs`:

```csharp
public class ArgumentParserConfigTests
{
    [Fact]
    public void ParsesSelectAsCommaSeparatedList()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--select", "NM,PF101"]);

        Assert.True(r.IsSuccess);
        Assert.Equal(["NM", "PF101"], r.Value!.Select);
    }

    [Fact]
    public void ParsesIgnore()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--ignore", "NM011"]);

        Assert.Equal(["NM011"], r.Value!.Ignore);
    }

    [Fact]
    public void ParsesConfigPath()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--config", "custom.toml"]);

        Assert.Equal("custom.toml", r.Value!.ConfigPath);
    }

    [Fact]
    public void FailOnIsNullWhenNotPassed()
    {
        var r = ArgumentParser.Parse(["check", "a.zip"]);

        Assert.Null(r.Value!.FailOn);
    }

    [Fact]
    public void FailOnIsCapturedWhenPassed()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--fail-on", "info"]);

        Assert.Equal(Severity.Info, r.Value!.FailOn);
    }

    [Fact]
    public void SelectWithoutValueFails()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--select"]);

        Assert.False(r.IsSuccess);
        Assert.Contains("--select", r.Error);
    }

    [Fact]
    public void SelectAndIgnoreDefaultToEmpty()
    {
        var r = ArgumentParser.Parse(["check", "a.zip"]);

        Assert.Empty(r.Value!.Select);
        Assert.Empty(r.Value.Ignore);
    }
}
```

- [ ] **Step 2: Atualizar o parser**

Em `src/PpLint.Cli/ArgumentParser.cs`:

Trocar o record por:

```csharp
public sealed record CliOptions(
    CliCommand Command,
    IReadOnlyList<string> Paths,
    string Format,
    string? Output,
    Severity? FailOn,
    bool NoColor,
    bool Quiet,
    string? ExplainRuleId,
    IReadOnlyList<string> Select,
    IReadOnlyList<string> Ignore,
    string? ConfigPath);
```

Dentro de `Parse`, declarar os novos acumuladores junto dos existentes:

```csharp
        var select = new List<string>();
        var ignore = new List<string>();
        string? configPath = null;
```

Trocar `var failOn = Severity.Error;` por:

```csharp
        Severity? failOn = null;
```

Acrescentar `"--select"`, `"--ignore"` e `"--config"` à lista de opções que exigem valor, no mesmo `case` de `--format`/`--output`/`--fail-on`, e tratar os valores:

```csharp
                case "--format":
                case "--output":
                case "--fail-on":
                case "--select":
                case "--ignore":
                case "--config":
                    if (i + 1 >= args.Length)
                        return ParseResult<CliOptions>.Fail($"A opção {arg} exige um valor.");
                    var value = args[++i];
                    switch (arg)
                    {
                        case "--format":
                            if (!ValidFormats.Contains(value))
                                return ParseResult<CliOptions>.Fail(
                                    $"Formato inválido: '{value}'. Válidos: {string.Join(", ", ValidFormats)}.");
                            format = value;
                            break;
                        case "--output":
                            output = value;
                            break;
                        case "--config":
                            configPath = value;
                            break;
                        case "--select":
                            select.AddRange(SplitList(value));
                            break;
                        case "--ignore":
                            ignore.AddRange(SplitList(value));
                            break;
                        default:
                            var parsed = ParseSeverity(value);
                            if (parsed is null)
                                return ParseResult<CliOptions>.Fail(
                                    $"Severidade inválida: '{value}'. Válidas: error, warning, info.");
                            failOn = parsed.Value;
                            break;
                    }
                    break;
```

Acrescentar o helper:

```csharp
    private static IEnumerable<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
```

Atualizar as duas construções de `CliOptions` (a de sucesso e a de `Ok(CliCommand)`) para passar os campos novos:

```csharp
        return ParseResult<CliOptions>.Ok(new CliOptions(
            command.Value, paths, format, output, failOn, noColor, quiet, explainRuleId,
            select, ignore, configPath));
```

```csharp
    private static ParseResult<CliOptions> Ok(CliCommand command) =>
        ParseResult<CliOptions>.Ok(new CliOptions(
            command, [], "text", null, null, false, false, null, [], [], null));
```

- [ ] **Step 3: Ajustar o teste da Fase 1 que assumia default no parser**

`CliOptions.FailOn` passou a ser `Severity?`, e o default deixou de ser decidido no parser — quem decide agora é o `ConfigResolver`, para que o arquivo possa mandar. Em `tests/PpLint.Cli.Tests/ArgumentParserTests.cs`, no teste `Parse_CheckWithSinglePath`, trocar:

```csharp
        Assert.Equal(Severity.Error, r.Value.FailOn);
```

por:

```csharp
        // Sem --fail-on, o parser não opina: o default vem do ConfigResolver.
        Assert.Null(r.Value.FailOn);
```

- [ ] **Step 4: Rodar os testes do parser**

Run: `dotnet test tests/PpLint.Cli.Tests --filter ArgumentParser`
Expected: PASSA — os 9 da Fase 1 (com o ajuste acima) e os 7 novos. `Parse_CheckWithMultiplePathsAndOptions` continua válido porque `--fail-on warning` segue produzindo `Severity.Warning`.

- [ ] **Step 5: Escrever o teste de integração**

`tests/PpLint.Cli.Tests/ConfigIntegrationTests.cs`:

```csharp
using System.IO.Compression;
using System.Text;

namespace PpLint.Cli.Tests;

public class ConfigIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pplint-int-{Guid.NewGuid():N}");

    public ConfigIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private const string ControlsJson = """
    {
      "TopParent": {
        "Name": "scrHome",
        "Template": { "Name": "screen" },
        "Children": [
          { "Name": "SalvarPedido", "Template": { "Name": "button" }, "Children": [] },
          { "Name": "ButtonCancelar", "Template": { "Name": "button" }, "Children": [] }
        ]
      }
    }
    """;

    private string CreateApp()
    {
        var path = Path.Combine(_dir, "App.msapp");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("Controls/1.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(ControlsJson);
        return path;
    }

    private (int Code, string Out, string Err) Invoke(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void WithoutConfig_UsesCamelPrefixAndFlagsPascalName()
    {
        var (_, output, _) = Invoke("check", CreateApp(), "--no-color");

        // 'ButtonCancelar' não começa com 'btn' no preset default.
        Assert.Contains("ButtonCancelar", output);
        Assert.Contains("SalvarPedido", output);
    }

    [Fact]
    public void PascalTypePreset_ChangesWhatIsFlagged()
    {
        File.WriteAllText(Path.Combine(_dir, "pp-lint.toml"), """
            [pp-lint]
            preset = "pascal-type"
            """);

        var (_, output, _) = Invoke("check", CreateApp(), "--no-color", "--config", Path.Combine(_dir, "pp-lint.toml"));

        // Agora 'ButtonCancelar' está correto e 'SalvarPedido' é que destoa.
        Assert.DoesNotContain("'ButtonCancelar'", output);
        Assert.Contains("SalvarPedido", output);
    }

    [Fact]
    public void IgnoreFromCommandLineSilencesRule()
    {
        var (_, output, _) = Invoke("check", CreateApp(), "--no-color", "--ignore", "NM011");

        Assert.DoesNotContain("NM011", output);
    }

    [Fact]
    public void SelectRunsOnlyTheChosenCategory()
    {
        var (_, output, _) = Invoke("check", CreateApp(), "--no-color", "--select", "PF");

        Assert.DoesNotContain("NM011", output);
    }

    [Fact]
    public void InvalidConfigExitsWithTwoAndExplains()
    {
        var config = Path.Combine(_dir, "quebrado.toml");
        File.WriteAllText(config, "[pp-lint\npreset =");

        var (code, _, err) = Invoke("check", CreateApp(), "--config", config);

        Assert.Equal(2, code);
        Assert.Contains("quebrado.toml", err);
    }

    [Fact]
    public void UnknownPresetExitsWithTwo()
    {
        var config = Path.Combine(_dir, "pp-lint.toml");
        File.WriteAllText(config, """
            [pp-lint]
            preset = "inventado"
            """);

        var (code, _, err) = Invoke("check", CreateApp(), "--config", config);

        Assert.Equal(2, code);
        Assert.Contains("inventado", err);
    }

    [Fact]
    public void MissingConfigFileExitsWithTwo()
    {
        var (code, _, err) = Invoke("check", CreateApp(), "--config", Path.Combine(_dir, "nao-existe.toml"));

        Assert.Equal(2, code);
        Assert.Contains("nao-existe.toml", err);
    }

    [Fact]
    public void WarnsAboutDefaultPresetWhenThereIsNoConfig()
    {
        var (_, _, err) = Invoke("check", CreateApp(), "--no-color");

        Assert.Contains("camel-prefix", err);
        Assert.Contains("pp-lint.toml", err);
    }

    [Fact]
    public void DoesNotWarnWhenConfigExists()
    {
        var config = Path.Combine(_dir, "pp-lint.toml");
        File.WriteAllText(config, """
            [pp-lint]
            preset = "camel-prefix"
            """);

        var (_, _, err) = Invoke("check", CreateApp(), "--no-color", "--config", config);

        Assert.DoesNotContain("camel-prefix", err);
    }

    [Fact]
    public void SuppressionDirectiveSilencesFinding()
    {
        var path = Path.Combine(_dir, "Suprimido.msapp");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Controls/1.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("""
            {
              "TopParent": {
                "Name": "scrHome",
                "Template": { "Name": "screen" },
                "Children": [
                  {
                    "Name": "SalvarPedido",
                    "Template": { "Name": "button" },
                    "Rules": [
                      { "Property": "OnSelect", "InvariantScript": "// pp-lint: disable=NM011\nNotify(\"ok\")" }
                    ],
                    "Children": []
                  }
                ]
              }
            }
            """);
        }

        var (_, output, _) = Invoke("check", path, "--no-color");

        Assert.DoesNotContain("NM011", output);
    }
}
```

- [ ] **Step 6: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Cli.Tests --filter ConfigIntegrationTests`
Expected: FALHA — o `Program` ainda usa `PpLintConfig.Default` e não conhece `--config`.

- [ ] **Step 7: Ligar tudo no Program**

Em `src/PpLint.Cli/Program.cs`, acrescentar os `using`:

```csharp
using PpLint.Core.Configuration;
using PpLint.Core.Suppression;
```

Substituir o método `RunCheck` inteiro por:

```csharp
    private static int RunCheck(CliOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (options.Format != "text")
        {
            stderr.WriteLine($"O formato '{options.Format}' será entregue na Fase 2c. Use 'text'.");
            return 2;
        }

        PpLintConfig config;
        bool usedConfigFile;
        try
        {
            (config, usedConfigFile) = LoadConfig(options);
        }
        catch (ConfigException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }

        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<Diagnostic>();
        var tallies = new List<RuleTally>();
        var engine = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly);

        foreach (var path in options.Paths)
        {
            PowerPlatformProject project;
            try
            {
                project = ProjectLoader.Load(path);
            }
            catch (ArtifactException ex)
            {
                stderr.WriteLine($"Erro ao ler '{path}': {ex.Message}");
                return 2;
            }

            var result = engine.Run(project, config, SuppressionIndex.Build(project));
            diagnostics.AddRange(result.Diagnostics);
            tallies.AddRange(result.Tallies);
        }

        stopwatch.Stop();

        var compliance = ComplianceScorer.Compute(tallies);
        var useColor = !options.NoColor && !Console.IsOutputRedirected;

        stdout.Write(TextReporter.Render(diagnostics, compliance, stopwatch.Elapsed, useColor, options.Quiet));

        if (!usedConfigFile && diagnostics.Any(d => d.Category == RuleCategory.Naming))
        {
            stderr.WriteLine(
                $"Nota: usando o preset de nomenclatura '{config.PresetName}' porque não há {ConfigLocator.FileName}. "
                + $"Presets disponíveis: {string.Join(", ", NamingPresets.Names)}. "
                + $"Crie um {ConfigLocator.FileName} com [pp-lint] preset = \"...\" para escolher outro.");
        }

        return diagnostics.Any(d => d.Severity >= config.FailOn) ? 1 : 0;
    }

    /// <summary>
    /// Monta a configuração final e informa se algum arquivo foi de fato usado —
    /// é o que decide se vale avisar sobre o preset default.
    /// </summary>
    private static (PpLintConfig Config, bool UsedFile) LoadConfig(CliOptions options)
    {
        string? path;

        if (options.ConfigPath is not null)
        {
            if (!File.Exists(options.ConfigPath))
                throw new ConfigException($"Arquivo de configuração não encontrado: '{options.ConfigPath}'.");
            path = options.ConfigPath;
        }
        else
        {
            path = ConfigLocator.Find(Directory.GetCurrentDirectory());
        }

        var file = ConfigFile.Empty;
        if (path is not null)
        {
            try
            {
                file = TomlConfigReader.Read(File.ReadAllText(path));
            }
            catch (ConfigException ex)
            {
                throw new ConfigException($"Erro em '{path}': {ex.Message}", ex);
            }
        }

        var cli = new CliOverrides(
            Select: options.Select.Count > 0 ? options.Select : null,
            Ignore: options.Ignore.Count > 0 ? options.Ignore : null,
            FailOn: options.FailOn);

        return (ConfigResolver.Resolve(file, cli), path is not null);
    }
```

- [ ] **Step 8: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA. Se `EndToEndTests.UnsupportedFormatExitsWithTwo` falhar, é porque a mensagem passou a dizer "Fase 2c" — ajuste a asserção do teste para `Assert.Contains("Fase 2c", err)`, já que a mensagem é a que mudou, não o comportamento.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: CLI monta configuração a partir de arquivo, preset e flags"
```

---

### Task 10: Documentação e validação contra os apps reais

**Files:**
- Create: `pp-lint.example.toml`
- Modify: `README.md`
- Modify: `PROGRESSO.md`
- Test: `tests/PpLint.Extractors.Tests/RealArtifactTests.cs` (acrescentar teste ao fim da classe)

**Interfaces:**
- Consumes: tudo das tasks anteriores.
- Produces: nenhuma API nova.

O fixture `tests/fixtures/chess-real.msapp` usa a convenção `pascal-type`. Com o preset certo, os 129 avisos de NM011 devem praticamente desaparecer — é essa queda que prova que a fase entregou o que prometeu.

- [ ] **Step 1: Escrever o teste de calibragem**

Acrescentar ao fim da classe `RealArtifactTests` em `tests/PpLint.Extractors.Tests/RealArtifactTests.cs`:

```csharp
    [Fact]
    public void RealMsapp_PascalTypePresetFitsTheAppFarBetter()
    {
        // O app real nomeia como ButtonCreateGame/LabelPlayers. Com o preset
        // default ele recebe mais de cem avisos de prefixo; com o preset que
        // corresponde à sua convenção, quase nenhum. É esse o ponto da Fase 2a.
        var project = ProjectLoader.Load(RealMsapp);
        var engine = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly);

        var camel = ConfigResolver.Resolve(
            new ConfigFile { Preset = "camel-prefix" }, CliOverrides.None);
        var pascal = ConfigResolver.Resolve(
            new ConfigFile { Preset = "pascal-type" }, CliOverrides.None);

        var comCamel = engine.Run(project, camel).Diagnostics.Count(d => d.RuleId == "NM011");
        var comPascal = engine.Run(project, pascal).Diagnostics.Count(d => d.RuleId == "NM011");

        Assert.True(comCamel > 50, $"esperava muitos avisos com camel-prefix, veio {comCamel}");
        Assert.True(
            comPascal < comCamel / 2,
            $"pascal-type devia reduzir bastante: camel={comCamel}, pascal={comPascal}");
    }
```

Acrescentar os `using` necessários ao topo do arquivo:

```csharp
using PpLint.Core.Configuration;
```

- [ ] **Step 2: Rodar e ver o número real**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter PascalTypePreset --logger "console;verbosity=detailed"`
Expected: PASSA. Se falhar, **não relaxe a asserção**: leia os nomes que continuam sendo apontados e ajuste a tabela `PascalPrefixes` em `NamingPresets` — o preset é que está mal calibrado contra a convenção real.

- [ ] **Step 3: Escrever o arquivo de exemplo**

`pp-lint.example.toml`:

```toml
# Copie para pp-lint.toml na raiz do repositório e ajuste.
# O pp-lint procura este arquivo no diretório atual e nos ancestrais.

[pp-lint]
# Convenção de nomenclatura. Opções: "camel-prefix" (btnSalvar) ou "pascal-type" (ButtonSalvar).
preset = "camel-prefix"

# Regras a executar. Vazio ou ausente = todas. Aceita ID (PF101) ou categoria (NM).
# select = ["NM", "PF"]

# Regras a nunca executar. Vence o select.
ignore = []

# Severidade a partir da qual o processo termina com código 1.
fail-on = "error"

# Ajustes pontuais de severidade, sem desligar a regra.
[pp-lint.severity-overrides]
# NM011 = "info"

# Sobrescreve apenas os padrões que você mencionar; o resto vem do preset.
[pp-lint.naming]
# global-variable = "^var[A-Z][A-Za-z0-9]*$"
# screen = "^scr[A-Z][A-Za-z0-9]*$"

# Prefixo esperado por tipo de controle. Também sobrescreve só o que mencionar.
[pp-lint.naming.control-prefixes]
# button = "btn"
# toggleSwitch = "tgl"

# Regras que não se aplicam a determinados artefatos.
[pp-lint.per-artifact-ignores]
# "**/Legado*.msapp" = ["NM010", "NM011"]
```

- [ ] **Step 4: Documentar no README**

Em `README.md`, acrescentar a seção abaixo logo após a seção "Regras da Fase 1":

```markdown
## Configuração

O pp-lint procura `pp-lint.toml` no diretório atual e nos ancestrais. Sem arquivo,
usa o preset `camel-prefix` e avisa uma vez qual está em uso. Veja
`pp-lint.example.toml` para um arquivo comentado.

```toml
[pp-lint]
preset = "pascal-type"
ignore = ["NM011"]
fail-on = "warning"
```

**Presets de nomenclatura.** Impor uma régua única faz o linter reclamar de apps
bem escritos que apenas seguem outro padrão — validando a Fase 1 contra um app
real, 129 avisos vinham só disso. Escolha o que corresponde à sua convenção:

| Preset | Controles | Variáveis |
|---|---|---|
| `camel-prefix` | `btnSalvar`, `lblTitulo` | `varTotal` |
| `pascal-type` | `ButtonSalvar`, `LabelTitulo` | `VarTotal` |

Precedência: preset → `pp-lint.toml` → flags de linha de comando.

## Silenciar um achado

Em Power Fx, um comentário na própria fórmula:

```powerapps
// pp-lint: disable=PF101
Set(varTemporaria, 1)
```

Em fluxos, JSON não aceita comentário — use o campo **descrição** da ação:

```
pp-lint: disable=FL201
```

A diretiva vale para o controle ou a ação onde aparece, e aceita vários IDs
separados por vírgula. Um achado suprimido sai do numerador **e** do denominador
do índice de conformidade: silenciar não aumenta a nota.

Para desligar uma regra inteira, use `ignore` no TOML ou `--ignore NM011`. Para
excluir artefatos específicos, `per-artifact-ignores`.
```

- [ ] **Step 5: Atualizar o PROGRESSO**

Substituir o conteúdo de `PROGRESSO.md` pela tabela da Fase 2a, mantendo a seção de desvios da Fase 1:

```markdown
# pp-lint — Progresso

Plano atual: `docs/superpowers/plans/2026-08-19-pp-lint-fase-2a.md`
Spec: `docs/superpowers/specs/2026-08-18-pp-lint-design.md`

## Fase 1 — concluída

17 tasks, validada contra 4 canvas apps reais da Microsoft.
Detalhes em `docs/superpowers/plans/2026-08-18-pp-lint-fase-1.md`.

## Fase 2a — configuração e supressão

| # | Task | Status |
|---|------|--------|
| 1 | Descrição da ação de fluxo | ⬜ |
| 2 | Índice de supressões | ⬜ |
| 3 | Motor aplica supressão | ⬜ |
| 4 | Presets de nomenclatura | ⬜ |
| 5 | Leitura do arquivo TOML | ⬜ |
| 6 | Resolver de configuração | ⬜ |
| 7 | Seleção de regras e ignores por artefato | ⬜ |
| 8 | Descoberta do arquivo de configuração | ⬜ |
| 9 | Integração no CLI | ⬜ |
| 10 | Documentação e validação contra os apps reais | ⬜ |

## Próximas fases

- **2b** — catálogo NM e PF completo, grafo de variáveis de contexto e coleções.
- **2c** — formatos JSON e SARIF, `explain` com documentação, índice por artefato.

## Pendência que depende do dono do projeto

Uma solução `.zip` exportada real em `tests/fixtures/solucao-exemplo.zip`. O caminho
de solução exportada — `solution.xml`, tabelas Dataverse, cloud flows — segue
validado apenas contra fixtures sintéticos, e todo o catálogo `FL*` da Fase 2b
depende dele.
```

- [ ] **Step 6: Validar manualmente contra o app real**

```bash
cd /c/PROJETOS/pp-lint
dotnet run --project src/PpLint.Cli -c Release -- check tests/fixtures/chess-real.msapp --no-color --quiet
dotnet run --project src/PpLint.Cli -c Release -- check tests/fixtures/chess-real.msapp --no-color --quiet --ignore NM011
```

Expected: a primeira execução mostra a nota sobre o preset default em stderr; a segunda não mostra NM011 no resumo e apresenta conformidade mais alta.

- [ ] **Step 7: Rodar tudo e commitar**

```bash
dotnet test
git add -A
git commit -m "docs: configuração e supressão no README; test: calibragem de preset no app real"
```

---

## Cobertura do spec nesta fase

Entregue: `pp-lint.toml` com descoberta em ancestrais, presets nomeados, precedência preset→arquivo→CLI, `--select`/`--ignore`/`--config`, `severity-overrides`, `per-artifact-ignores`, supressão inline em Power Fx e em fluxos, exclusão de suprimidos do índice, erro explícito para config inválida.

Adiado, com a fase de destino:

| Item do spec | Fase |
|---|---|
| Catálogo completo NM e PF | 2b |
| Grafo de variáveis de contexto e coleções | 2b |
| Formatos JSON, SARIF, HTML, Markdown | 2c e 5 |
| `pp-lint explain` com documentação das regras | 2c |
| `pp-lint inspect` | 2c |
| Índice de conformidade por artefato | 2c |
| Ingestão do `AppCheckerResult.sarif` | 2c |
| Baseline | 5 |
