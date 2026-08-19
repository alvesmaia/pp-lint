# pp-lint Fase 2b-1 — Variáveis

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entender variáveis de verdade — globais, de contexto e coleções, cada uma com seu escopo — e entregar as 8 regras que dependem disso.

**Architecture:** Duas peças novas. `SymbolResolver` responde "este identificador é o quê?" (controle, tela, componente, data source, função, enum, escopo de linha, ou candidato a variável), montado a partir do IR mais os nomes que o engine do Power Fx conhece. `VariableGraph` passa a carregar tipo e escopo, e usa o resolvedor para separar variável de tudo o mais. As regras só consultam o grafo.

**Tech Stack:** .NET 10, C# 14, xUnit, `Microsoft.PowerFx.Core`, `Tomlyn`.

**Spec:** `docs/superpowers/specs/2026-08-18-pp-lint-design.md` (seção 7.2, regras PF102–PF106; seção 7.1, regras NM001–NM003)

## Decisões desta fase

Tomadas com autonomia delegada pelo dono do projeto; qualquer uma pode ser contestada depois.

1. **Resolver identificadores pelo IR mais os built-ins do engine.** `Engine.GetAllFunctionNames()` dá os nomes de função; controles, telas, componentes e data sources vêm do IR; enums do Power Fx entram por lista embutida, porque o engine não os expõe numa API estável.
2. **Variáveis de contexto têm escopo de tela**, que é a semântica real do Power Fx. Uma `locFiltro` definida em `scrPedidos` e referenciada em `scrDetalhe` não conta como uso. `Navigate(scrDestino, Fade, {locX: 1})` define a variável na tela **de destino**.
3. **Coleções e globais são de app inteiro**, sem escopo de tela.
4. **`ThisItem`, `ThisRecord`, `Self`, `Parent`, `As` e os campos de `With`/`ForAll` são escopos de linha, não variáveis.** Sem isso a PF104 acusaria cada galeria do app.
5. **PF104 entra por último e é calibrada contra o app real.** É a regra de maior valor e maior risco de falso positivo; o `chess-real.msapp`, com 827 fórmulas, é o juiz.
6. **PF106 tem escopo estreito**: dois `Set` do mesmo nome, com o mesmo valor literal, na mesma expressão, sem leitura entre eles. Detectar redundância entre expressões diferentes exigiria ordem de execução, que o IR não modela.

## Global Constraints

- **.NET 10** (`net10.0`), `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **Somente-leitura absoluto.** Nada fora de `--output` é aberto para escrita.
- **Dependências de runtime:** apenas `Microsoft.PowerFx.Core` e `Tomlyn`.
- **Mensagens em português do Brasil**, com acentuação correta.
- **IDs de regra são imutáveis.**
- **Invariante do índice:** no máximo uma violação por alvo avaliado (`V(r) ≤ E(r)`).
- **Achado suprimido continua contando como violação** — suprimir tira o achado do relatório, não o débito da nota.
- **Toda regra chama `ctx.Evaluated(n)`** para cada alvo examinado.
- **Regra nova precisa de teste positivo e negativo.** Falso positivo destrói a confiança mais rápido que regra faltando.
- **TDD obrigatório.** Commit ao fim de cada task.

## Estrutura de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/PpLint.PowerFx/SymbolKind.cs` | O que um identificador é |
| `src/PpLint.PowerFx/PowerFxBuiltins.cs` | Funções (do engine) e enums (lista) do Power Fx |
| `src/PpLint.PowerFx/SymbolResolver.cs` | Classifica um identificador, dado o app |
| `src/PpLint.PowerFx/RowScopeCollector.cs` | Nomes introduzidos por `As`, `With`, `ForAll` numa expressão |
| `src/PpLint.PowerFx/VariableGraph.cs` | *(reescrever)* definições e leituras com tipo e escopo |
| `src/PpLint.Rules/Naming/VariableNamingRules.cs` | NM001, NM002, NM003 |
| `src/PpLint.Rules/Fx/UnusedContextVariableRule.cs` | PF102 |
| `src/PpLint.Rules/Fx/UnusedCollectionRule.cs` | PF103 |
| `src/PpLint.Rules/Fx/UndefinedVariableRule.cs` | PF104 |
| `src/PpLint.Rules/Fx/GlobalUsedInSingleScreenRule.cs` | PF105 |
| `src/PpLint.Rules/Fx/RedundantSetRule.cs` | PF106 |

---

### Task 1: Built-ins do Power Fx

**Files:**
- Create: `src/PpLint.PowerFx/PowerFxBuiltins.cs`
- Test: `tests/PpLint.PowerFx.Tests/PowerFxBuiltinsTests.cs`

**Interfaces:**
- Consumes: `Microsoft.PowerFx.Engine`.
- Produces: `static class PowerFxBuiltins` com `static IReadOnlySet<string> FunctionNames`, `static IReadOnlySet<string> EnumNames`, `static bool IsBuiltin(string name)`.

Os nomes de função vêm de `Engine.GetAllFunctionNames()`, então acompanham a versão do pacote sem manutenção. Os enums entram por lista: o engine não os expõe numa API estável, e são poucos e estáveis no produto.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.PowerFx.Tests/PowerFxBuiltinsTests.cs`:

```csharp
namespace PpLint.PowerFx.Tests;

public class PowerFxBuiltinsTests
{
    [Theory]
    [InlineData("If")]
    [InlineData("Set")]
    [InlineData("Filter")]
    [InlineData("Notify")]
    [InlineData("IsBlank")]
    public void KnownFunctionsAreBuiltin(string name) =>
        Assert.Contains(name, PowerFxBuiltins.FunctionNames);

    [Fact]
    public void FunctionNamesComeFromTheEngine()
    {
        // Se a lista viesse hardcoded, envelheceria a cada atualização do pacote.
        Assert.True(PowerFxBuiltins.FunctionNames.Count > 100,
            $"esperava centenas de funções, veio {PowerFxBuiltins.FunctionNames.Count}");
    }

    [Theory]
    [InlineData("Color")]
    [InlineData("Align")]
    [InlineData("Font")]
    [InlineData("DisplayMode")]
    [InlineData("ScreenTransition")]
    [InlineData("SortOrder")]
    public void KnownEnumsAreBuiltin(string name) =>
        Assert.Contains(name, PowerFxBuiltins.EnumNames);

    [Fact]
    public void IsBuiltinCoversFunctionsAndEnums()
    {
        Assert.True(PowerFxBuiltins.IsBuiltin("Filter"));
        Assert.True(PowerFxBuiltins.IsBuiltin("Color"));
        Assert.False(PowerFxBuiltins.IsBuiltin("varTotal"));
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        // Power Fx não diferencia maiúsculas em nomes de função.
        Assert.True(PowerFxBuiltins.IsBuiltin("filter"));
        Assert.True(PowerFxBuiltins.IsBuiltin("COLOR"));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter PowerFxBuiltinsTests`
Expected: FALHA de compilação — `PowerFxBuiltins` não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.PowerFx/PowerFxBuiltins.cs`:

```csharp
using Microsoft.PowerFx;

namespace PpLint.PowerFx;

/// <summary>
/// Nomes que o Power Fx já conhece. Um identificador built-in nunca é uma
/// variável do app — sem essa distinção, toda chamada de função viraria
/// "variável não definida".
/// </summary>
public static class PowerFxBuiltins
{
    /// <summary>
    /// Enums do Power Fx. Vêm de lista porque o engine não os expõe numa API
    /// pública estável; em compensação mudam pouco e a lista é curta.
    /// </summary>
    private static readonly string[] Enums =
    [
        "Color", "Align", "VerticalAlign", "Font", "FontWeight", "Underline",
        "DisplayMode", "ScreenTransition", "SortOrder", "Layout", "LayoutDirection",
        "Overflow", "BorderStyle", "TextPosition", "TextMode", "TextFormat",
        "DateTimeFormat", "MatchOptions", "Match", "ErrorKind", "FormMode",
        "SubmitMode", "Direction", "Transition", "ZoomMode", "PenMode",
        "GridStyle", "ImagePosition", "LabelPosition", "Notify", "NotificationType",
        "SelectedState", "MapStyle", "Compass", "Orientation", "ExternalMessage",
    ];

    static PowerFxBuiltins()
    {
        var engine = new Engine(new PowerFxConfig());

        FunctionNames = engine.GetAllFunctionNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        EnumNames = Enums.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> FunctionNames { get; }

    public static IReadOnlySet<string> EnumNames { get; }

    public static bool IsBuiltin(string name) =>
        FunctionNames.Contains(name) || EnumNames.Contains(name);
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter PowerFxBuiltinsTests`
Expected: PASSA — 13 testes. Se `KnownEnumsAreBuiltin` falhar para algum nome, acrescente-o à lista `Enums`; se `FunctionNamesComeFromTheEngine` falhar, `GetAllFunctionNames` mudou de assinatura — ajuste a chamada, nunca o limite do teste.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: catálogo de funções e enums built-in do Power Fx"
```

---

### Task 2: Escopos de linha

**Files:**
- Create: `src/PpLint.PowerFx/RowScopeCollector.cs`
- Test: `tests/PpLint.PowerFx.Tests/RowScopeCollectorTests.cs`

**Interfaces:**
- Consumes: `PowerFxParser`, `AstWalker`, `Microsoft.PowerFx.Syntax`.
- Produces: `static IReadOnlySet<string> RowScopeCollector.Collect(TexlNode root)`.

Numa galeria, `ThisItem.Title` referencia o escopo de linha, não uma variável. O mesmo vale para `Gallery1.AllItems As item` e para os campos de `With({total: 10}, total)`. Sem reconhecer esses nomes, a PF104 reporta cada um deles.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.PowerFx.Tests/RowScopeCollectorTests.cs`:

```csharp
namespace PpLint.PowerFx.Tests;

public class RowScopeCollectorTests
{
    private static IReadOnlySet<string> Collect(string script) =>
        RowScopeCollector.Collect(PowerFxParser.Parse(script).Root!);

    [Fact]
    public void ImplicitScopesAreAlwaysPresent()
    {
        var scopes = Collect("1 + 1");

        Assert.Contains("ThisItem", scopes);
        Assert.Contains("ThisRecord", scopes);
        Assert.Contains("Self", scopes);
        Assert.Contains("Parent", scopes);
    }

    [Fact]
    public void AsIntroducesAName()
    {
        Assert.Contains("pedido", Collect("ForAll(Pedidos As pedido, pedido.Total)"));
    }

    [Fact]
    public void WithFieldsBecomeScopeNames()
    {
        Assert.Contains("total", Collect("With({total: 10}, total * 2)"));
    }

    [Fact]
    public void WithSeveralFields()
    {
        var scopes = Collect("With({a: 1, b: 2}, a + b)");

        Assert.Contains("a", scopes);
        Assert.Contains("b", scopes);
    }

    [Fact]
    public void NestedAsIsCollected()
    {
        var scopes = Collect("ForAll(A As x, ForAll(B As y, x.Id & y.Id))");

        Assert.Contains("x", scopes);
        Assert.Contains("y", scopes);
    }

    [Fact]
    public void OrdinaryIdentifiersAreNotScopes()
    {
        Assert.DoesNotContain("varTotal", Collect("Set(varTotal, 1)"));
    }

    [Fact]
    public void UpdateContextRecordIsNotAScope()
    {
        // UpdateContext também recebe um record, mas seus campos são variáveis
        // de contexto. Tratá-los como escopo faria PF102 e NM002 cegarem.
        Assert.DoesNotContain("locFiltro", Collect("UpdateContext({locFiltro: 1})"));
    }

    [Fact]
    public void NavigateContextRecordIsNotAScope()
    {
        Assert.DoesNotContain("locId", Collect("Navigate(scrB, Fade, {locId: 7})"));
    }

    [Fact]
    public void ComparisonIsCaseInsensitive()
    {
        Assert.Contains("PEDIDO", Collect("ForAll(Pedidos As pedido, pedido.Total)"));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter RowScopeCollectorTests`
Expected: FALHA de compilação — `RowScopeCollector` não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.PowerFx/RowScopeCollector.cs`:

```csharp
using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx;

/// <summary>
/// Nomes que existem só dentro da expressão: ThisItem numa galeria, o apelido
/// de um `As`, os campos de um `With`. Não são variáveis do app e não podem
/// ser cobrados como tal.
/// </summary>
public static class RowScopeCollector
{
    /// <summary>
    /// Nomes que o Power Fx introduz sozinho. "Value" entra porque é a coluna
    /// implícita de tabelas de valor único — Concat([1,2], Value) é código correto.
    /// </summary>
    private static readonly string[] Implicit =
        ["ThisItem", "ThisRecord", "Self", "Parent", "ThisProperty", "Value"];

    public static IReadOnlySet<string> Collect(TexlNode root)
    {
        var scopes = new HashSet<string>(Implicit, StringComparer.OrdinalIgnoreCase);

        // Pedidos As pedido
        foreach (var asNode in AstWalker.Descendants(root).OfType<AsNode>())
        {
            var alias = asNode.Right.Name.Value;
            if (!string.IsNullOrEmpty(alias))
                scopes.Add(alias);
        }

        // With({total: 10}, total * 2) — só o record do With vira escopo.
        // Um record qualquer NÃO serve: UpdateContext({locFiltro: 1}) também é
        // um record, e tratar seus campos como escopo faria as variáveis de
        // contexto sumirem do grafo — PF102 e NM002 nunca veriam nada.
        foreach (var call in AstWalker.Calls(root, "With"))
        {
            var args = call.Args?.ChildNodes;
            if (args is null || args.Count == 0 || args[0] is not RecordNode record)
                continue;

            foreach (var id in record.Ids)
            {
                var field = id.Name.Value;
                if (!string.IsNullOrEmpty(field))
                    scopes.Add(field);
            }
        }

        return scopes;
    }
}
```

> Se `AsNode.Right` ou `RecordNode.Ids` tiverem outro nome no pacote instalado, ajuste essas linhas aqui e o uso equivalente em `VariableGraph.AddFromRecordArgument` (Task 4) — a assinatura de `Collect` é o que as tasks seguintes consomem. Descubra os nomes reais com o mesmo método da Fase 1: um teste temporário que imprime `node.GetType().GetProperties()`.

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter RowScopeCollectorTests`
Expected: PASSA — 7 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: coleta de escopos de linha em expressões Power Fx"
```

---

### Task 3: Resolvedor de símbolos

**Files:**
- Create: `src/PpLint.PowerFx/SymbolKind.cs`, `src/PpLint.PowerFx/SymbolResolver.cs`
- Test: `tests/PpLint.PowerFx.Tests/SymbolResolverTests.cs`

**Interfaces:**
- Consumes: `PowerFxBuiltins`, `CanvasApp`, `Control`, `DataSource`.
- Produces:
  - `enum SymbolKind { Unknown, Control, Screen, DataSource, Function, Enum, RowScope }`.
  - `SymbolResolver` com `static SymbolResolver Build(CanvasApp app)`, `SymbolKind Resolve(string name, IReadOnlySet<string> rowScopes)`, `bool IsVariableCandidate(string name, IReadOnlySet<string> rowScopes)`.

`Unknown` é o único resultado que permite chamar o nome de variável. Tudo o mais tem dono conhecido.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.PowerFx.Tests/SymbolResolverTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx.Tests;

public class SymbolResolverTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static CanvasApp AppWith()
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);
        app.DataSources.Add(new DataSource("Pedidos", "SharePoint", ["Title"]));
        return app;
    }

    private static readonly IReadOnlySet<string> NoScopes = new HashSet<string>();

    [Fact]
    public void ControlIsResolved() =>
        Assert.Equal(SymbolKind.Control, SymbolResolver.Build(AppWith()).Resolve("btnOk", NoScopes));

    [Fact]
    public void ScreenIsResolved() =>
        Assert.Equal(SymbolKind.Screen, SymbolResolver.Build(AppWith()).Resolve("scrHome", NoScopes));

    [Fact]
    public void DataSourceIsResolved() =>
        Assert.Equal(SymbolKind.DataSource, SymbolResolver.Build(AppWith()).Resolve("Pedidos", NoScopes));

    [Fact]
    public void FunctionIsResolved() =>
        Assert.Equal(SymbolKind.Function, SymbolResolver.Build(AppWith()).Resolve("Filter", NoScopes));

    [Fact]
    public void EnumIsResolved() =>
        Assert.Equal(SymbolKind.Enum, SymbolResolver.Build(AppWith()).Resolve("Color", NoScopes));

    [Fact]
    public void RowScopeIsResolved()
    {
        var scopes = new HashSet<string>(["pedido"], StringComparer.OrdinalIgnoreCase);

        Assert.Equal(SymbolKind.RowScope, SymbolResolver.Build(AppWith()).Resolve("pedido", scopes));
    }

    [Fact]
    public void UnknownNameStaysUnknown() =>
        Assert.Equal(SymbolKind.Unknown, SymbolResolver.Build(AppWith()).Resolve("varTotal", NoScopes));

    [Fact]
    public void OnlyUnknownIsVariableCandidate()
    {
        var resolver = SymbolResolver.Build(AppWith());

        Assert.True(resolver.IsVariableCandidate("varTotal", NoScopes));
        Assert.False(resolver.IsVariableCandidate("btnOk", NoScopes));
        Assert.False(resolver.IsVariableCandidate("Filter", NoScopes));
        Assert.False(resolver.IsVariableCandidate("Color", NoScopes));
    }

    [Fact]
    public void ResolutionIsCaseInsensitive()
    {
        var resolver = SymbolResolver.Build(AppWith());

        Assert.Equal(SymbolKind.Control, resolver.Resolve("BTNOK", NoScopes));
        Assert.Equal(SymbolKind.DataSource, resolver.Resolve("pedidos", NoScopes));
    }

    [Fact]
    public void RowScopeWinsOverEverythingElse()
    {
        // Um `As` pode sombrear um nome de controle dentro daquela expressão.
        var scopes = new HashSet<string>(["btnOk"], StringComparer.OrdinalIgnoreCase);

        Assert.Equal(SymbolKind.RowScope, SymbolResolver.Build(AppWith()).Resolve("btnOk", scopes));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter SymbolResolverTests`
Expected: FALHA de compilação — `SymbolResolver` não existe.

- [ ] **Step 3: Implementar o enum**

`src/PpLint.PowerFx/SymbolKind.cs`:

```csharp
namespace PpLint.PowerFx;

/// <summary>
/// O que um identificador é. Unknown é o único que permite tratá-lo como
/// variável do app — todo o resto tem dono conhecido.
/// </summary>
public enum SymbolKind
{
    Unknown,
    Control,
    Screen,
    DataSource,
    Function,
    Enum,
    RowScope,
}
```

- [ ] **Step 4: Implementar o resolvedor**

`src/PpLint.PowerFx/SymbolResolver.cs`:

```csharp
using PpLint.Core.Model;

namespace PpLint.PowerFx;

/// <summary>
/// Responde "este identificador é o quê?" para um app. Sem essa distinção,
/// uma regra que procura variáveis não definidas acusaria todo controle,
/// data source e função que a fórmula referencia.
/// </summary>
public sealed class SymbolResolver
{
    private readonly IReadOnlySet<string> _controls;
    private readonly IReadOnlySet<string> _screens;
    private readonly IReadOnlySet<string> _dataSources;

    private SymbolResolver(
        IReadOnlySet<string> controls,
        IReadOnlySet<string> screens,
        IReadOnlySet<string> dataSources)
    {
        _controls = controls;
        _screens = screens;
        _dataSources = dataSources;
    }

    public static SymbolResolver Build(CanvasApp app)
    {
        var controls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var screens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var control in app.AllControls())
        {
            if (control.IsScreen)
                screens.Add(control.Name);
            else
                controls.Add(control.Name);
        }

        var dataSources = app.DataSources
            .Select(d => d.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new SymbolResolver(controls, screens, dataSources);
    }

    /// <summary>
    /// A ordem importa: um `As` sombreia qualquer outro nome dentro da
    /// expressão onde aparece, então escopo de linha é testado primeiro.
    /// </summary>
    public SymbolKind Resolve(string name, IReadOnlySet<string> rowScopes)
    {
        if (rowScopes.Contains(name))
            return SymbolKind.RowScope;

        if (_screens.Contains(name))
            return SymbolKind.Screen;

        if (_controls.Contains(name))
            return SymbolKind.Control;

        if (_dataSources.Contains(name))
            return SymbolKind.DataSource;

        if (PowerFxBuiltins.FunctionNames.Contains(name))
            return SymbolKind.Function;

        if (PowerFxBuiltins.EnumNames.Contains(name))
            return SymbolKind.Enum;

        return SymbolKind.Unknown;
    }

    public bool IsVariableCandidate(string name, IReadOnlySet<string> rowScopes) =>
        Resolve(name, rowScopes) == SymbolKind.Unknown;
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter SymbolResolverTests`
Expected: PASSA — 10 testes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: resolvedor de símbolos do canvas app"
```

---

### Task 4: Grafo de variáveis com tipo e escopo

**Files:**
- Modify: `src/PpLint.PowerFx/VariableGraph.cs` (reescrever)
- Test: `tests/PpLint.PowerFx.Tests/VariableGraphTests.cs` (reescrever)

**Interfaces:**
- Consumes: `SymbolResolver`, `RowScopeCollector`, `PowerFxParser`, `AstWalker`, `CanvasApp`, `Control`, `PowerFxProperty`, `SourceLocation`.
- Produces:
  - `enum VariableKind { Global, Context, Collection }`.
  - `sealed record VariableDefinition(string Name, VariableKind Kind, string? Screen, SourceLocation Location)` — `Screen` só é preenchido para `Context`.
  - `VariableGraph` com `static VariableGraph Build(CanvasApp app)`, `IReadOnlyList<VariableDefinition> Definitions`, `bool IsRead(string name)`, `bool IsReadInScreen(string name, string screen)`, `IReadOnlyList<string> ScreensReading(string name)`, `IReadOnlyList<VariableReference> UnresolvedReads`.
  - `sealed record VariableReference(string Name, SourceLocation Location)`.

**Compatibilidade.** `IsRead(string)` e a propriedade `Globals` continuam existindo com o mesmo comportamento, porque a PF101 já em produção depende delas. `Globals` passa a ser derivada: `Definitions.Where(d => d.Kind == Global)`.

**De onde vem cada definição:**

| Origem | Tipo | Escopo |
|---|---|---|
| `Set(nome, …)` | Global | app |
| `UpdateContext({nome: …})` | Context | tela onde a fórmula está |
| `Navigate(scrDestino, …, {nome: …})` | Context | **tela de destino** |
| `Collect`/`ClearCollect`/`Clear`/`ClearCollect(nome, …)` | Collection | app |

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.PowerFx.Tests/VariableGraphTests.cs` — substituir o arquivo inteiro:

```csharp
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx.Tests;

public class VariableGraphTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    /// <summary>App com duas telas; as fórmulas entram na tela indicada.</summary>
    private static CanvasApp AppWith(params (string Screen, string Property, string Script)[] formulas)
    {
        var app = new CanvasApp { Name = "App", Location = Loc(null) };

        foreach (var screenName in formulas.Select(f => f.Screen).Distinct())
        {
            var screen = new Control
            {
                Name = screenName,
                TemplateName = "screen",
                IsScreen = true,
                Location = Loc(screenName),
            };

            var button = new Control
            {
                Name = $"btn{screenName}",
                TemplateName = "button",
                Location = Loc($"btn{screenName}"),
            };

            foreach (var (_, property, script) in formulas.Where(f => f.Screen == screenName))
                button.Properties.Add(new PowerFxProperty(property, script, Loc($"btn{screenName}.{property}")));

            screen.AddChild(button);
            app.Screens.Add(screen);
        }

        return app;
    }

    [Fact]
    public void SetProducesGlobal()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Set(varTotal, 1)")));

        var v = Assert.Single(graph.Definitions);
        Assert.Equal("varTotal", v.Name);
        Assert.Equal(VariableKind.Global, v.Kind);
        Assert.Null(v.Screen);
    }

    [Fact]
    public void UpdateContextProducesContextVariableOnItsOwnScreen()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "UpdateContext({locFiltro: 1})")));

        var v = Assert.Single(graph.Definitions);
        Assert.Equal("locFiltro", v.Name);
        Assert.Equal(VariableKind.Context, v.Kind);
        Assert.Equal("scrA", v.Screen);
    }

    [Fact]
    public void NavigateDefinesContextVariableOnTheDestinationScreen()
    {
        // A variável nasce na tela de destino, não na de origem.
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Navigate(scrB, Fade, {locId: 7})")));

        var v = Assert.Single(graph.Definitions);
        Assert.Equal("locId", v.Name);
        Assert.Equal(VariableKind.Context, v.Kind);
        Assert.Equal("scrB", v.Screen);
    }

    [Fact]
    public void ClearCollectProducesCollection()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "ClearCollect(colItens, Pedidos)")));

        var v = Assert.Single(graph.Definitions);
        Assert.Equal("colItens", v.Name);
        Assert.Equal(VariableKind.Collection, v.Kind);
    }

    [Fact]
    public void CollectAlsoProducesCollection()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Collect(colItens, {Id: 1})")));

        Assert.Equal(VariableKind.Collection, Assert.Single(graph.Definitions).Kind);
    }

    [Fact]
    public void ReadIsDetected()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "Text", "varTotal")));

        Assert.True(graph.IsRead("varTotal"));
    }

    [Fact]
    public void DefinitionAloneIsNotRead()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Set(varTotal, 1)")));

        Assert.False(graph.IsRead("varTotal"));
    }

    [Fact]
    public void ReadIsAttributedToItsScreen()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrB", "Text", "varTotal")));

        Assert.True(graph.IsReadInScreen("varTotal", "scrB"));
        Assert.False(graph.IsReadInScreen("varTotal", "scrA"));
    }

    [Fact]
    public void ScreensReadingListsEveryScreen()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "Text", "varTotal"),
            ("scrB", "Text", "varTotal")));

        Assert.Equal(["scrA", "scrB"], graph.ScreensReading("varTotal").Order());
    }

    [Fact]
    public void ControlNameIsNotAnUnresolvedRead()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "Text", "btnscrA.Text")));

        Assert.Empty(graph.UnresolvedReads);
    }

    [Fact]
    public void FunctionNameIsNotAnUnresolvedRead()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "Text", "Text(Now(), \"dd/mm\")")));

        Assert.Empty(graph.UnresolvedReads);
    }

    [Fact]
    public void RowScopeIsNotAnUnresolvedRead()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "Text", "ThisItem.Title")));

        Assert.Empty(graph.UnresolvedReads);
    }

    [Fact]
    public void ReadOfNeverDefinedNameIsUnresolved()
    {
        var graph = VariableGraph.Build(AppWith(("scrA", "Text", "varNuncaDefinida")));

        var unresolved = Assert.Single(graph.UnresolvedReads);
        Assert.Equal("varNuncaDefinida", unresolved.Name);
    }

    [Fact]
    public void DefinedVariableIsNotUnresolved()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "Text", "varTotal")));

        Assert.Empty(graph.UnresolvedReads);
    }

    [Fact]
    public void DuplicateDefinitionsAppearOnce()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "OnChange", "Set(varTotal, 2)")));

        Assert.Single(graph.Definitions);
    }

    [Fact]
    public void GlobalsPropertyStillWorksForPF101()
    {
        // A PF101 já em produção depende desta propriedade.
        var graph = VariableGraph.Build(AppWith(("scrA", "OnSelect", "Set(varTotal, 1)")));

        Assert.Equal(["varTotal"], graph.Globals.Select(g => g.Name));
    }

    [Fact]
    public void AppLevelFormulasAreIncluded()
    {
        var app = AppWith(("scrA", "Text", "varUsuario"));
        app.AppProperties.Add(new PowerFxProperty("OnStart", "Set(varUsuario, User().Email)", Loc("App.OnStart")));

        var graph = VariableGraph.Build(app);

        Assert.Single(graph.Definitions);
        Assert.True(graph.IsRead("varUsuario"));
    }

    [Fact]
    public void UnparseableFormulaIsIgnored()
    {
        var graph = VariableGraph.Build(AppWith(
            ("scrA", "OnSelect", "Set(varTotal, 1)"),
            ("scrA", "Text", "Set(varOutra,")));

        Assert.Single(graph.Definitions);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter VariableGraphTests`
Expected: FALHA de compilação — `VariableKind`, `Definitions`, `IsReadInScreen`, `ScreensReading` e `UnresolvedReads` não existem.

- [ ] **Step 3: Reescrever o grafo**

`src/PpLint.PowerFx/VariableGraph.cs` — substituir o arquivo inteiro:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx;

public enum VariableKind { Global, Context, Collection }

/// <summary>Uma variável e onde ela nasceu. Screen só vale para Context.</summary>
public sealed record VariableDefinition(
    string Name,
    VariableKind Kind,
    string? Screen,
    SourceLocation Location);

public sealed record VariableReference(string Name, SourceLocation Location);

/// <summary>
/// Variáveis de um canvas app, com tipo e escopo. Globais e coleções valem no
/// app inteiro; variáveis de contexto pertencem a uma tela — e uma definida via
/// Navigate nasce na tela de destino, não na de origem.
/// </summary>
public sealed class VariableGraph
{
    private readonly Dictionary<string, VariableDefinition> _definitions;
    private readonly Dictionary<string, HashSet<string>> _readsByScreen;
    private readonly List<VariableReference> _unresolved;

    private VariableGraph(
        Dictionary<string, VariableDefinition> definitions,
        Dictionary<string, HashSet<string>> readsByScreen,
        List<VariableReference> unresolved)
    {
        _definitions = definitions;
        _readsByScreen = readsByScreen;
        _unresolved = unresolved;
    }

    public IReadOnlyList<VariableDefinition> Definitions => _definitions.Values.ToList();

    /// <summary>Mantida para a PF101, que já está em produção.</summary>
    public IReadOnlyList<VariableDefinition> Globals =>
        _definitions.Values.Where(d => d.Kind == VariableKind.Global).ToList();

    public IReadOnlyList<VariableReference> UnresolvedReads => _unresolved;

    public bool IsRead(string name) => _readsByScreen.ContainsKey(name);

    public bool IsReadInScreen(string name, string screen) =>
        _readsByScreen.TryGetValue(name, out var screens) && screens.Contains(screen);

    public IReadOnlyList<string> ScreensReading(string name) =>
        _readsByScreen.TryGetValue(name, out var screens) ? screens.ToList() : [];

    public static VariableGraph Build(CanvasApp app)
    {
        var resolver = SymbolResolver.Build(app);
        var definitions = new Dictionary<string, VariableDefinition>(StringComparer.OrdinalIgnoreCase);
        var readsByScreen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var candidateReads = new List<(string Name, SourceLocation Location)>();

        foreach (var (property, screen) in AllFormulas(app))
        {
            var parsed = PowerFxParser.Parse(property.Script);
            if (parsed.Root is null)
                continue;

            var rowScopes = RowScopeCollector.Collect(parsed.Root);
            var defined = new HashSet<TexlNode>();

            CollectDefinitions(parsed.Root, property, screen, definitions, defined);

            foreach (var identifier in AstWalker.Identifiers(parsed.Root))
            {
                if (defined.Contains(identifier))
                    continue;

                var name = identifier.Ident.Name.Value;

                if (!resolver.IsVariableCandidate(name, rowScopes))
                    continue;

                if (!readsByScreen.TryGetValue(name, out var screens))
                    readsByScreen[name] = screens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (screen is not null)
                    screens.Add(screen);

                candidateReads.Add((name, property.Location));
            }
        }

        var unresolved = candidateReads
            .Where(r => !definitions.ContainsKey(r.Name))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new VariableReference(g.Key, g.First().Location))
            .ToList();

        return new VariableGraph(definitions, readsByScreen, unresolved);
    }

    private static void CollectDefinitions(
        TexlNode root,
        PowerFxProperty property,
        string? screen,
        Dictionary<string, VariableDefinition> definitions,
        HashSet<TexlNode> defined)
    {
        foreach (var call in AstWalker.Calls(root, "Set"))
            AddFromFirstArgument(call, VariableKind.Global, screen: null, property, definitions, defined);

        foreach (var name in new[] { "Collect", "ClearCollect", "Clear" })
            foreach (var call in AstWalker.Calls(root, name))
                AddFromFirstArgument(call, VariableKind.Collection, screen: null, property, definitions, defined);

        foreach (var call in AstWalker.Calls(root, "UpdateContext"))
            AddFromRecordArgument(call, argumentIndex: 0, screen, property, definitions);

        // Navigate(destino, transição, {contexto}) — a variável nasce no destino.
        foreach (var call in AstWalker.Calls(root, "Navigate"))
        {
            var destino = FirstArgumentName(call);
            AddFromRecordArgument(call, argumentIndex: 2, destino ?? screen, property, definitions);
        }
    }

    private static void AddFromFirstArgument(
        CallNode call,
        VariableKind kind,
        string? screen,
        PowerFxProperty property,
        Dictionary<string, VariableDefinition> definitions,
        HashSet<TexlNode> defined)
    {
        var args = call.Args?.ChildNodes;
        if (args is null || args.Count == 0 || args[0] is not FirstNameNode target)
            return;

        defined.Add(target);
        Add(definitions, target.Ident.Name.Value, kind, screen, property.Location);
    }

    private static void AddFromRecordArgument(
        CallNode call,
        int argumentIndex,
        string? screen,
        PowerFxProperty property,
        Dictionary<string, VariableDefinition> definitions)
    {
        var args = call.Args?.ChildNodes;
        if (args is null || args.Count <= argumentIndex || args[argumentIndex] is not RecordNode record)
            return;

        foreach (var id in record.Ids)
            Add(definitions, id.Name.Value, VariableKind.Context, screen, property.Location);
    }

    private static string? FirstArgumentName(CallNode call)
    {
        var args = call.Args?.ChildNodes;
        return args is { Count: > 0 } && args[0] is FirstNameNode name ? name.Ident.Name.Value : null;
    }

    private static void Add(
        Dictionary<string, VariableDefinition> definitions,
        string name,
        VariableKind kind,
        string? screen,
        SourceLocation location)
    {
        if (string.IsNullOrEmpty(name) || definitions.ContainsKey(name))
            return;

        definitions[name] = new VariableDefinition(name, kind, screen, location);
    }

    /// <summary>Cada fórmula do app com a tela a que pertence (null para App.OnStart).</summary>
    private static IEnumerable<(PowerFxProperty Property, string? Screen)> AllFormulas(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return (property, null);

        foreach (var screen in app.Screens)
            foreach (var control in screen.SelfAndDescendants())
                foreach (var property in control.Properties)
                    yield return (property, screen.Name);
    }
}
```

- [ ] **Step 4: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — os 18 novos de `VariableGraphTests` e, principalmente, os 6 de `UnusedGlobalVariableRuleTests`, que provam que a PF101 não regrediu.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: grafo de variáveis com tipo, escopo de tela e leituras resolvidas"
```

---

### Task 5: NM001, NM002 e NM003 — nomenclatura de variáveis

**Files:**
- Create: `src/PpLint.Rules/Naming/VariableNamingRules.cs`
- Test: `tests/PpLint.Rules.Tests/VariableNamingRuleTests.cs`

**Interfaces:**
- Consumes: `VariableGraph`, `VariableKind`, `VariableDefinition`, `IRule`, `LintContext`, `NamingConfig`.
- Produces: `GlobalVariableNamingRule` (NM001, Naming, Warning), `ContextVariableNamingRule` (NM002, Naming, Warning), `CollectionNamingRule` (NM003, Naming, Warning) — as três no mesmo arquivo, porque diferem apenas no tipo de variável e no regex consultado.

Cada regra avalia uma variável por vez e compara o nome com o regex do preset em vigor (`Naming.GlobalVariable`, `Naming.ContextVariable`, `Naming.Collection`). Regex inválido no config é erro do usuário e precisa dizer isso — não pode derrubar a análise nem passar batido.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/VariableNamingRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Naming;

namespace PpLint.Rules.Tests;

public class VariableNamingRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static PowerPlatformProject ProjectWithFormula(string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnOk.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);
        return project;
    }

    private static LintResult Run(IRule rule, string script, PpLintConfig? config = null) =>
        new RuleEngine([rule]).Run(ProjectWithFormula(script), config ?? PpLintConfig.Default);

    [Fact]
    public void NM001_ReportsGlobalOutsideTheConvention()
    {
        var d = Assert.Single(Run(new GlobalVariableNamingRule(), "Set(total, 1)").Diagnostics);

        Assert.Equal("NM001", d.RuleId);
        Assert.Contains("total", d.Message);
        Assert.Contains("var", d.Message);
    }

    [Fact]
    public void NM001_AcceptsConventionalName() =>
        Assert.Empty(Run(new GlobalVariableNamingRule(), "Set(varTotal, 1)").Diagnostics);

    [Fact]
    public void NM001_IgnoresContextAndCollections()
    {
        var result = Run(new GlobalVariableNamingRule(), "UpdateContext({errado: 1}); ClearCollect(tambemErrado, [1])");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void NM002_ReportsContextVariableOutsideTheConvention()
    {
        var d = Assert.Single(Run(new ContextVariableNamingRule(), "UpdateContext({filtro: 1})").Diagnostics);

        Assert.Equal("NM002", d.RuleId);
        Assert.Contains("loc", d.Message);
    }

    [Fact]
    public void NM002_AcceptsConventionalName() =>
        Assert.Empty(Run(new ContextVariableNamingRule(), "UpdateContext({locFiltro: 1})").Diagnostics);

    [Fact]
    public void NM003_ReportsCollectionOutsideTheConvention()
    {
        var d = Assert.Single(Run(new CollectionNamingRule(), "ClearCollect(itens, [1])").Diagnostics);

        Assert.Equal("NM003", d.RuleId);
        Assert.Contains("col", d.Message);
    }

    [Fact]
    public void NM003_AcceptsConventionalName() =>
        Assert.Empty(Run(new CollectionNamingRule(), "ClearCollect(colItens, [1])").Diagnostics);

    [Fact]
    public void PascalTypePresetChangesWhatIsAccepted()
    {
        var config = PpLintConfig.Default with
        {
            Naming = new NamingConfig { GlobalVariable = "^Var[A-Z][A-Za-z0-9]*$" },
        };

        Assert.Empty(Run(new GlobalVariableNamingRule(), "Set(VarTotal, 1)", config).Diagnostics);
        Assert.Single(Run(new GlobalVariableNamingRule(), "Set(varTotal, 1)", config).Diagnostics);
    }

    [Fact]
    public void EvaluatesOneTargetPerVariable()
    {
        var result = Run(new GlobalVariableNamingRule(), "Set(varA, 1); Set(errado, 2)");

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void InvalidRegexInConfigIsReportedNotCrashed()
    {
        // Regex quebrado é erro do usuário; a regra não pode derrubar a análise
        // nem fingir que está tudo certo.
        var config = PpLintConfig.Default with
        {
            Naming = new NamingConfig { GlobalVariable = "^var([A-Z" },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            Run(new GlobalVariableNamingRule(), "Set(varTotal, 1)", config));

        Assert.Contains("global-variable", ex.Message);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter VariableNamingRuleTests`
Expected: FALHA de compilação — as três regras não existem.

- [ ] **Step 3: Implementar as três regras**

`src/PpLint.Rules/Naming/VariableNamingRules.cs`:

```csharp
using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Naming;

/// <summary>
/// Base das regras que comparam o nome de uma variável com o regex do preset.
/// As três diferem apenas no tipo de variável e em qual padrão consultam.
/// </summary>
public abstract class VariableNamingRuleBase : IRule
{
    protected abstract VariableKind Kind { get; }

    protected abstract string PatternOf(NamingConfig naming);

    /// <summary>Nome da chave no TOML, para que a mensagem de erro diga o que corrigir.</summary>
    protected abstract string ConfigKey { get; }

    protected abstract string Describe { get; }

    public void Check(LintContext ctx)
    {
        var pattern = PatternOf(ctx.Config.Naming);
        Regex regex;

        try
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"Expressão regular inválida em '{ConfigKey}': {ex.Message}", ex);
        }

        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Definitions.Where(d => d.Kind == Kind))
            {
                ctx.Evaluated(1);

                if (!regex.IsMatch(variable.Name))
                {
                    ctx.Report(
                        variable.Location,
                        $"{Describe} '{variable.Name}' não segue a convenção do preset "
                        + $"'{ctx.Config.PresetName}' ({pattern}). Ajuste o nome ou '{ConfigKey}' no pp-lint.toml.");
                }
            }
        }
    }
}

/// <summary>NM001 — variável global fora da convenção de nomes.</summary>
[Rule("NM001", RuleCategory.Naming, Severity.Warning)]
public sealed class GlobalVariableNamingRule : VariableNamingRuleBase
{
    protected override VariableKind Kind => VariableKind.Global;
    protected override string PatternOf(NamingConfig naming) => naming.GlobalVariable;
    protected override string ConfigKey => "global-variable";
    protected override string Describe => "A variável global";
}

/// <summary>NM002 — variável de contexto fora da convenção de nomes.</summary>
[Rule("NM002", RuleCategory.Naming, Severity.Warning)]
public sealed class ContextVariableNamingRule : VariableNamingRuleBase
{
    protected override VariableKind Kind => VariableKind.Context;
    protected override string PatternOf(NamingConfig naming) => naming.ContextVariable;
    protected override string ConfigKey => "context-variable";
    protected override string Describe => "A variável de contexto";
}

/// <summary>NM003 — coleção fora da convenção de nomes.</summary>
[Rule("NM003", RuleCategory.Naming, Severity.Warning)]
public sealed class CollectionNamingRule : VariableNamingRuleBase
{
    protected override VariableKind Kind => VariableKind.Collection;
    protected override string PatternOf(NamingConfig naming) => naming.Collection;
    protected override string ConfigKey => "collection";
    protected override string Describe => "A coleção";
}
```

- [ ] **Step 4: Ajustar o teste de contrato do catálogo**

O teste `EveryRuleCountsEvaluatedTargetsOnARepresentativeProject`, em `tests/PpLint.Rules.Tests/RuleCatalogContractTests.cs`, exige que toda regra examine ao menos um alvo no projeto de exemplo. As regras novas precisam de variáveis dos três tipos. Substituir a fórmula do controle `btnSalvar` nesse teste por:

```csharp
        var app = App("A", Screen("scrHome",
            Ctl("btnSalvar", "button",
                ("OnSelect",
                 "Set(varTotal, 1); UpdateContext({locAberto: true}); ClearCollect(colItens, [1]); "
                 + "If(varTotal > 0, Notify(\"ok\"))"))));
```

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — 10 testes novos e o contrato do catálogo satisfeito pelas três regras.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: regras NM001, NM002 e NM003 de nomenclatura de variáveis"
```

---

### Task 6: PF102 e PF103 — contexto e coleção não usadas

**Files:**
- Create: `src/PpLint.Rules/Fx/UnusedContextVariableRule.cs`, `src/PpLint.Rules/Fx/UnusedCollectionRule.cs`
- Test: `tests/PpLint.Rules.Tests/UnusedVariableRuleTests.cs`

**Interfaces:**
- Consumes: `VariableGraph`, `VariableKind`, `IRule`, `LintContext`.
- Produces: `UnusedContextVariableRule` (PF102, PowerFx, Warning), `UnusedCollectionRule` (PF103, PowerFx, Warning).

**A diferença que importa.** A PF102 usa `IsReadInScreen`, não `IsRead`: uma variável de contexto só está viva na tela dela. Uma `locFiltro` definida em `scrPedidos` e referenciada em `scrDetalhe` **não** conta como uso — no Power Fx aquilo lê outra coisa (ou nada). A PF103, por coleções serem de app inteiro, usa `IsRead`.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/UnusedVariableRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class UnusedVariableRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static PowerPlatformProject ProjectWith(params (string Screen, string Property, string Script)[] formulas)
    {
        var app = new CanvasApp { Name = "App", Location = Loc(null) };

        foreach (var screenName in formulas.Select(f => f.Screen).Distinct())
        {
            var screen = new Control
            {
                Name = screenName,
                TemplateName = "screen",
                IsScreen = true,
                Location = Loc(screenName),
            };
            var button = new Control
            {
                Name = $"btn{screenName}",
                TemplateName = "button",
                Location = Loc($"btn{screenName}"),
            };

            foreach (var (_, property, script) in formulas.Where(f => f.Screen == screenName))
                button.Properties.Add(new PowerFxProperty(property, script, Loc($"btn{screenName}.{property}")));

            screen.AddChild(button);
            app.Screens.Add(screen);
        }

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);
        return project;
    }

    private static LintResult Run(IRule rule, PowerPlatformProject project) =>
        new RuleEngine([rule]).Run(project, PpLintConfig.Default);

    [Fact]
    public void PF102_ReportsContextVariableNeverRead()
    {
        var project = ProjectWith(("scrA", "OnSelect", "UpdateContext({locFiltro: 1})"));
        var d = Assert.Single(Run(new UnusedContextVariableRule(), project).Diagnostics);

        Assert.Equal("PF102", d.RuleId);
        Assert.Contains("locFiltro", d.Message);
    }

    [Fact]
    public void PF102_AcceptsReadOnTheSameScreen()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "UpdateContext({locFiltro: 1})"),
            ("scrA", "Text", "locFiltro"));

        Assert.Empty(Run(new UnusedContextVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void PF102_ReadOnAnotherScreenDoesNotCount()
    {
        // Variável de contexto vive numa tela só; ler o mesmo nome em outra
        // tela lê outra coisa, e a definição original continua sem uso.
        var project = ProjectWith(
            ("scrA", "OnSelect", "UpdateContext({locFiltro: 1})"),
            ("scrB", "Text", "locFiltro"));

        Assert.Single(Run(new UnusedContextVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void PF102_NavigateContextIsReadOnTheDestination()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "Navigate(scrB, Fade, {locId: 7})"),
            ("scrB", "Text", "locId"));

        Assert.Empty(Run(new UnusedContextVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void PF102_IgnoresGlobalsAndCollections()
    {
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, 1); ClearCollect(colY, [1])"));
        var result = Run(new UnusedContextVariableRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void PF103_ReportsCollectionNeverUsed()
    {
        var project = ProjectWith(("scrA", "OnSelect", "ClearCollect(colItens, [1])"));
        var d = Assert.Single(Run(new UnusedCollectionRule(), project).Diagnostics);

        Assert.Equal("PF103", d.RuleId);
        Assert.Contains("colItens", d.Message);
    }

    [Fact]
    public void PF103_AcceptsUseOnAnyScreen()
    {
        // Coleção é de app inteiro: usar em outra tela conta.
        var project = ProjectWith(
            ("scrA", "OnSelect", "ClearCollect(colItens, [1])"),
            ("scrB", "Items", "colItens"));

        Assert.Empty(Run(new UnusedCollectionRule(), project).Diagnostics);
    }

    [Fact]
    public void PF103_EvaluatesOneTargetPerCollection()
    {
        var project = ProjectWith(("scrA", "OnSelect", "ClearCollect(colA, [1]); ClearCollect(colB, [2])"));
        var result = Run(new UnusedCollectionRule(), project);

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(2, tally.Violations);
    }

    [Fact]
    public void PF103_EvaluatesZeroWhenThereAreNoCollections()
    {
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, 1)"));

        Assert.Equal(0, Assert.Single(Run(new UnusedCollectionRule(), project).Tallies).Evaluated);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UnusedVariableRuleTests`
Expected: FALHA de compilação — as duas regras não existem.

- [ ] **Step 3: Implementar PF102**

`src/PpLint.Rules/Fx/UnusedContextVariableRule.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF102 — variável de contexto criada e nunca lida na tela onde vive.
/// A checagem é por tela porque é assim que o Power Fx funciona: o mesmo nome
/// em outra tela é outra variável, e ler lá não mantém esta viva.
/// </summary>
[Rule("PF102", RuleCategory.PowerFx, Severity.Warning)]
public sealed class UnusedContextVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Definitions.Where(d => d.Kind == VariableKind.Context))
            {
                ctx.Evaluated(1);

                if (variable.Screen is null || !graph.IsReadInScreen(variable.Name, variable.Screen))
                {
                    ctx.Report(
                        variable.Location,
                        $"A variável de contexto '{variable.Name}' é definida e nunca lida na tela "
                        + $"'{variable.Screen ?? "(desconhecida)"}'. Remova a definição ou use o valor.");
                }
            }
        }
    }
}
```

- [ ] **Step 4: Implementar PF103**

`src/PpLint.Rules/Fx/UnusedCollectionRule.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF103 — coleção criada e nunca usada. Coleções são do app inteiro, então
/// basta ser lida em qualquer tela. Uma coleção órfã ocupa memória do cliente
/// e costuma sobrar de uma tela que mudou de ideia.
/// </summary>
[Rule("PF103", RuleCategory.PowerFx, Severity.Warning)]
public sealed class UnusedCollectionRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Definitions.Where(d => d.Kind == VariableKind.Collection))
            {
                ctx.Evaluated(1);

                if (!graph.IsRead(variable.Name))
                {
                    ctx.Report(
                        variable.Location,
                        $"A coleção '{variable.Name}' é criada e nunca usada. "
                        + "Remova o Collect/ClearCollect ou passe a consumir os dados.");
                }
            }
        }
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UnusedVariableRuleTests`
Expected: PASSA — 9 testes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: regras PF102 e PF103 de contexto e coleção não usadas"
```

---

### Task 7: PF105 e PF106 — global de uma tela só e Set redundante

**Files:**
- Create: `src/PpLint.Rules/Fx/GlobalUsedInSingleScreenRule.cs`, `src/PpLint.Rules/Fx/RedundantSetRule.cs`
- Test: `tests/PpLint.Rules.Tests/GlobalScopeRuleTests.cs`

**Interfaces:**
- Consumes: `VariableGraph`, `ScreensReading`, `PowerFxParser`, `AstWalker`, `IRule`, `LintContext`.
- Produces: `GlobalUsedInSingleScreenRule` (PF105, PowerFx, Info), `RedundantSetRule` (PF106, PowerFx, Warning).

**PF105** é `Info`, não `Warning`: usar global onde caberia contexto não quebra nada, só amplia o alcance de um estado sem necessidade. Só reporta quando a variável é lida em exatamente uma tela — global nunca lida é assunto da PF101, e reportar as duas coisas seria punir o mesmo problema duas vezes.

**PF106** tem escopo estreito, como decidido: dois `Set` do mesmo nome com o mesmo valor literal, dentro da mesma expressão. Detectar redundância entre expressões diferentes exigiria ordem de execução, que o IR não modela — e chutar isso geraria falso positivo em código correto.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/GlobalScopeRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class GlobalScopeRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static PowerPlatformProject ProjectWith(params (string Screen, string Property, string Script)[] formulas)
    {
        var app = new CanvasApp { Name = "App", Location = Loc(null) };

        foreach (var screenName in formulas.Select(f => f.Screen).Distinct())
        {
            var screen = new Control
            {
                Name = screenName,
                TemplateName = "screen",
                IsScreen = true,
                Location = Loc(screenName),
            };
            var button = new Control
            {
                Name = $"btn{screenName}",
                TemplateName = "button",
                Location = Loc($"btn{screenName}"),
            };

            foreach (var (_, property, script) in formulas.Where(f => f.Screen == screenName))
                button.Properties.Add(new PowerFxProperty(property, script, Loc($"btn{screenName}.{property}")));

            screen.AddChild(button);
            app.Screens.Add(screen);
        }

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);
        return project;
    }

    private static LintResult Run(IRule rule, PowerPlatformProject project) =>
        new RuleEngine([rule]).Run(project, PpLintConfig.Default);

    [Fact]
    public void PF105_ReportsGlobalReadOnASingleScreen()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "Set(varFiltro, 1)"),
            ("scrA", "Text", "varFiltro"));

        var d = Assert.Single(Run(new GlobalUsedInSingleScreenRule(), project).Diagnostics);

        Assert.Equal("PF105", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
        Assert.Contains("varFiltro", d.Message);
        Assert.Contains("scrA", d.Message);
    }

    [Fact]
    public void PF105_AcceptsGlobalReadOnSeveralScreens()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "Set(varUsuario, 1)"),
            ("scrA", "Text", "varUsuario"),
            ("scrB", "Text", "varUsuario"));

        Assert.Empty(Run(new GlobalUsedInSingleScreenRule(), project).Diagnostics);
    }

    [Fact]
    public void PF105_IgnoresGlobalNeverRead()
    {
        // Global sem leitura nenhuma é assunto da PF101; contar aqui puniria
        // o mesmo problema duas vezes no índice.
        var project = ProjectWith(("scrA", "OnSelect", "Set(varOrfa, 1)"));
        var result = Run(new GlobalUsedInSingleScreenRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void PF105_IgnoresContextAndCollections()
    {
        var project = ProjectWith(
            ("scrA", "OnSelect", "UpdateContext({locX: 1}); ClearCollect(colY, [1])"),
            ("scrA", "Text", "locX & Concat(colY, Value)"));

        Assert.Empty(Run(new GlobalUsedInSingleScreenRule(), project).Diagnostics);
    }

    [Fact]
    public void PF106_ReportsSameSetTwiceInTheSameFormula()
    {
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, 1); Set(varX, 1)"));
        var d = Assert.Single(Run(new RedundantSetRule(), project).Diagnostics);

        Assert.Equal("PF106", d.RuleId);
        Assert.Contains("varX", d.Message);
    }

    [Fact]
    public void PF106_AcceptsDifferentValues() =>
        Assert.Empty(Run(new RedundantSetRule(),
            ProjectWith(("scrA", "OnSelect", "Set(varX, 1); Set(varX, 2)"))).Diagnostics);

    [Fact]
    public void PF106_AcceptsDifferentVariables() =>
        Assert.Empty(Run(new RedundantSetRule(),
            ProjectWith(("scrA", "OnSelect", "Set(varX, 1); Set(varY, 1)"))).Diagnostics);

    [Fact]
    public void PF106_AcceptsSameSetInDifferentFormulas()
    {
        // Sem ordem de execução não dá para afirmar que uma torna a outra inútil.
        var project = ProjectWith(
            ("scrA", "OnSelect", "Set(varX, 1)"),
            ("scrA", "OnChange", "Set(varX, 1)"));

        Assert.Empty(Run(new RedundantSetRule(), project).Diagnostics);
    }

    [Fact]
    public void PF106_AcceptsNonLiteralValue()
    {
        // Set(varX, Now()) duas vezes produz valores diferentes.
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, Now()); Set(varX, Now())"));

        Assert.Empty(Run(new RedundantSetRule(), project).Diagnostics);
    }

    [Fact]
    public void PF106_EvaluatesOnePerSetPair()
    {
        var project = ProjectWith(("scrA", "OnSelect", "Set(varX, 1); Set(varX, 1)"));

        Assert.Equal(1, Assert.Single(Run(new RedundantSetRule(), project).Tallies).Evaluated);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter GlobalScopeRuleTests`
Expected: FALHA de compilação — as duas regras não existem.

- [ ] **Step 3: Implementar PF105**

`src/PpLint.Rules/Fx/GlobalUsedInSingleScreenRule.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF105 — variável global lida em uma única tela. Não quebra nada, mas amplia
/// o alcance de um estado sem necessidade: uma variável de contexto expressaria
/// melhor a intenção e morreria junto com a tela.
/// Global nunca lida é assunto da PF101 e não é avaliada aqui.
/// </summary>
[Rule("PF105", RuleCategory.PowerFx, Severity.Info)]
public sealed class GlobalUsedInSingleScreenRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Definitions.Where(d => d.Kind == VariableKind.Global))
            {
                var screens = graph.ScreensReading(variable.Name);
                if (screens.Count == 0)
                    continue;

                ctx.Evaluated(1);

                if (screens.Count == 1)
                {
                    ctx.Report(
                        variable.Location,
                        $"A variável global '{variable.Name}' só é lida na tela '{screens[0]}'. "
                        + "Uma variável de contexto (UpdateContext) limitaria o estado a essa tela.");
                }
            }
        }
    }
}
```

- [ ] **Step 4: Implementar PF106**

`src/PpLint.Rules/Fx/RedundantSetRule.cs`:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF106 — a mesma variável recebe o mesmo valor literal duas vezes na mesma
/// fórmula. O segundo Set não faz nada.
/// Só olha dentro de uma fórmula: entre fórmulas diferentes não há ordem de
/// execução conhecida, e afirmar redundância ali seria chute.
/// </summary>
[Rule("PF106", RuleCategory.PowerFx, Severity.Warning)]
public sealed class RedundantSetRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var property in AllProperties(app))
            {
                var parsed = PowerFxParser.Parse(property.Script);
                if (parsed.Root is null)
                    continue;

                var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var call in AstWalker.Calls(parsed.Root, "Set"))
                {
                    var args = call.Args?.ChildNodes;
                    if (args is null || args.Count < 2 || args[0] is not FirstNameNode target)
                        continue;

                    var literal = LiteralText(args[1]);
                    if (literal is null)
                        continue;

                    var name = target.Ident.Name.Value;

                    if (seen.TryGetValue(name, out var anterior) && anterior == literal)
                    {
                        ctx.Evaluated(1);

                        if (reported.Add(name))
                        {
                            ctx.Report(
                                property.Location,
                                $"A variável '{name}' recebe o mesmo valor ({literal}) duas vezes nesta fórmula. "
                                + "O segundo Set não tem efeito.");
                        }
                    }
                    else
                    {
                        seen[name] = literal;
                    }
                }
            }
        }
    }

    /// <summary>Texto do valor quando ele é literal; null quando pode variar entre execuções.</summary>
    private static string? LiteralText(TexlNode node) => node switch
    {
        NumLitNode or DecLitNode or StrLitNode or BoolLitNode => node.ToString(),
        _ => null,
    };

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter GlobalScopeRuleTests`
Expected: PASSA — 10 testes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: regras PF105 e PF106 de escopo de global e Set redundante"
```

---

### Task 8: PF104 — variável nunca definida, calibrada contra o app real

**Files:**
- Create: `src/PpLint.Rules/Fx/UndefinedVariableRule.cs`
- Test: `tests/PpLint.Rules.Tests/UndefinedVariableRuleTests.cs`
- Test: `tests/PpLint.Extractors.Tests/RealArtifactTests.cs` (acrescentar teste ao fim da classe)

**Interfaces:**
- Consumes: `VariableGraph.UnresolvedReads`, `VariableReference`, `IRule`, `LintContext`.
- Produces: `UndefinedVariableRule` (PF104, PowerFx, Error).

**Esta é a regra de maior risco da fase.** Ela reporta identificadores que não são controle, tela, data source, função, enum, escopo de linha nem variável definida. Se o resolvedor tiver qualquer buraco, ela acusa código correto — e um erro (severidade `Error`) falso quebra o build de quem confiou na ferramenta.

Por isso ela entra por último, e o `chess-real.msapp` — 827 fórmulas reais — é o juiz. O teste de calibragem exige poucos achados; se vierem muitos, o defeito está no resolvedor, não no limite do teste.

- [ ] **Step 1: Escrever o teste unitário**

`tests/PpLint.Rules.Tests/UndefinedVariableRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class UndefinedVariableRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static PowerPlatformProject ProjectWith(string script, params string[] dataSources)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnOk.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);
        foreach (var ds in dataSources)
            app.DataSources.Add(new DataSource(ds, "SharePoint", ["Title"]));

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);
        return project;
    }

    private static LintResult Run(string script, params string[] dataSources) =>
        new RuleEngine([new UndefinedVariableRule()]).Run(ProjectWith(script, dataSources), PpLintConfig.Default);

    [Fact]
    public void ReportsNameThatWasNeverDefined()
    {
        var d = Assert.Single(Run("Notify(varNuncaDefinida)").Diagnostics);

        Assert.Equal("PF104", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("varNuncaDefinida", d.Message);
    }

    [Fact]
    public void AcceptsDefinedVariable() =>
        Assert.Empty(Run("Set(varTotal, 1); Notify(varTotal)").Diagnostics);

    [Fact]
    public void AcceptsControlReference() =>
        Assert.Empty(Run("Notify(btnOk.Text)").Diagnostics);

    [Fact]
    public void AcceptsScreenReference() =>
        Assert.Empty(Run("Navigate(scrHome)").Diagnostics);

    [Fact]
    public void AcceptsDataSource() =>
        Assert.Empty(Run("ClearCollect(colX, Filter(Pedidos, true))", "Pedidos").Diagnostics);

    [Fact]
    public void AcceptsBuiltinFunction() =>
        Assert.Empty(Run("Notify(Text(Now(), \"dd/mm\"))").Diagnostics);

    [Fact]
    public void AcceptsEnum() =>
        Assert.Empty(Run("Notify(\"oi\", NotificationType.Success)").Diagnostics);

    [Fact]
    public void AcceptsRowScope() =>
        Assert.Empty(Run("ForAll([1,2] As n, Notify(n))").Diagnostics);

    [Fact]
    public void AcceptsImplicitValueColumn() =>
        Assert.Empty(Run("Notify(Concat([1,2], Value))").Diagnostics);

    [Fact]
    public void AcceptsThisItem() =>
        Assert.Empty(Run("Notify(ThisItem.Title)").Diagnostics);

    [Fact]
    public void ReportsEachUndefinedNameOnce()
    {
        var result = Run("Notify(varTypo); Notify(varTypo)");

        Assert.Single(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Violations);
    }

    [Fact]
    public void EvaluatesOneTargetPerCandidateRead()
    {
        var result = Run("Set(varOk, 1); Notify(varOk); Notify(varTypo)");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UndefinedVariableRuleTests`
Expected: FALHA de compilação — `UndefinedVariableRule` não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.Rules/Fx/UndefinedVariableRule.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF104 — identificador lido que não é variável definida, controle, tela,
/// data source, função, enum nem escopo de linha. Quase sempre é erro de
/// digitação, e no Power Fx isso vira branco em silêncio em vez de erro.
///
/// O denominador é o número de nomes distintos que chegaram a ser candidatos a
/// variável: só assim o índice mede "quantos nomes lidos existem de verdade".
/// </summary>
[Rule("PF104", RuleCategory.PowerFx, Severity.Error)]
public sealed class UndefinedVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            var definidas = graph.Definitions
                .Select(d => d.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var naoDefinidas = graph.UnresolvedReads
                .Select(r => r.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            ctx.Evaluated(definidas.Count + naoDefinidas.Count);

            foreach (var referencia in graph.UnresolvedReads)
            {
                ctx.Report(
                    referencia.Location,
                    $"'{referencia.Name}' é lido mas nunca definido — não é variável, controle, "
                    + "tela, fonte de dados nem função conhecida. Verifique se o nome está correto.");
            }
        }
    }
}
```

- [ ] **Step 4: Rodar o teste unitário**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UndefinedVariableRuleTests`
Expected: PASSA — 11 testes.

- [ ] **Step 5: Escrever o teste de calibragem contra o app real**

Acrescentar ao fim da classe `RealArtifactTests`, em `tests/PpLint.Extractors.Tests/RealArtifactTests.cs`:

```csharp
    [Fact]
    public void RealMsapp_UndefinedVariableRuleDoesNotFloodWithFalsePositives()
    {
        // PF104 tem severidade Error: um falso positivo quebra o build de quem
        // confiou na ferramenta. Este app tem 827 fórmulas reais e funciona —
        // um punhado de achados é plausível, dezenas significam buraco no
        // resolvedor de símbolos, não app ruim.
        var project = ProjectLoader.Load(RealMsapp);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        var achados = result.Diagnostics.Where(d => d.RuleId == "PF104").ToList();

        Assert.True(
            achados.Count <= 5,
            "PF104 achou nomes demais num app real que funciona; o resolvedor está deixando "
            + "passar alguma categoria de símbolo: "
            + string.Join(", ", achados.Take(15).Select(d => d.Message)));
    }
```

- [ ] **Step 6: Rodar a calibragem e investigar o que aparecer**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter UndefinedVariableRule --logger "console;verbosity=detailed"`

Se falhar, leia os nomes reportados e classifique cada um:

| O que é | Correção |
|---|---|
| Nome de componente canvas | `SymbolResolver` precisa indexar `app.Components` |
| Propriedade customizada de componente | idem |
| Enum do Power Fx fora da lista | acrescentar em `PowerFxBuiltins.Enums` |
| Escopo de linha não coberto | ampliar `RowScopeCollector` |
| Variável definida por caminho não modelado | ampliar `VariableGraph.CollectDefinitions` |
| Erro de digitação de verdade no app | achado legítimo, mantenha |

**Nunca relaxe o limite do teste para fazê-lo passar** — o limite é o contrato de confiança da regra.

- [ ] **Step 7: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA, incluindo o contrato do catálogo, que agora cobre 13 regras.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: regra PF104 de variável nunca definida, calibrada contra app real"
```

---

### Task 9: Documentação e verificação final

**Files:**
- Modify: `README.md`
- Modify: `PROGRESSO.md`
- Modify: `pp-lint.example.toml`

**Interfaces:**
- Consumes: tudo das tasks anteriores.
- Produces: nenhuma API nova.

- [ ] **Step 1: Atualizar a tabela de regras no README**

Em `README.md`, substituir a seção "## Regras da Fase 1" inteira por:

```markdown
## Regras

| Id | Regra | Severidade |
|---|---|---|
| NM001 | Variável global fora da convenção (`varTotal`) | Aviso |
| NM002 | Variável de contexto fora da convenção (`locFiltro`) | Aviso |
| NM003 | Coleção fora da convenção (`colItens`) | Aviso |
| NM010 | Controle com nome padrão do Studio (`Button1`, `Screen1`) | Erro |
| NM011 | Prefixo do controle não corresponde ao tipo, inclusive prefixo de **outro** tipo | Aviso |
| PF101 | Variável global definida e nunca lida | Aviso |
| PF102 | Variável de contexto definida e nunca lida **na tela dela** | Aviso |
| PF103 | Coleção criada e nunca usada | Aviso |
| PF104 | Nome lido mas nunca definido — quase sempre erro de digitação | Erro |
| PF105 | Variável global lida em uma única tela; caberia contexto | Informação |
| PF106 | Mesmo `Set` com o mesmo valor duas vezes na mesma fórmula | Aviso |
| PF110 | Condição constante (`If(2 > 1, …)`, `If(true, …)`) | Erro |
| FL201 | Variável de fluxo inicializada e nunca lida | Aviso |

`pp-lint rules` lista o catálogo instalado.

### Como o pp-lint entende variáveis

Variáveis de contexto pertencem a uma tela — é assim que o Power Fx funciona.
Uma `locFiltro` criada em `scrPedidos` e referenciada em `scrDetalhe` não conta
como uso: lá é outra variável. `Navigate(scrDestino, Fade, {locId: 7})` cria a
variável na tela **de destino**, e é lá que o pp-lint procura por leituras.

Globais e coleções valem no app inteiro.

Para saber se um identificador é variável, o pp-lint primeiro descarta o que tem
dono conhecido: controles, telas, componentes, fontes de dados, funções e enums do
Power Fx, e escopos de linha (`ThisItem`, `Self`, `Parent`, apelidos de `As`,
campos de `With`). O que sobra é candidato a variável — e é isso que permite a
PF104 apontar erro de digitação sem acusar cada galeria do app.
```

- [ ] **Step 2: Documentar as chaves de nomenclatura que passaram a valer**

Em `pp-lint.example.toml`, substituir o bloco de `[pp-lint.naming]` por:

```toml
# Sobrescreve apenas os padrões que você mencionar; o resto vem do preset.
# global-variable, context-variable e collection valem desde a Fase 2b-1
# (regras NM001, NM002 e NM003). screen e component ainda não têm regra.
[pp-lint.naming]
# global-variable = "^var[A-Z][A-Za-z0-9]*$"
# context-variable = "^loc[A-Z][A-Za-z0-9]*$"
# collection = "^col[A-Z][A-Za-z0-9]*$"
```

- [ ] **Step 3: Atualizar o PROGRESSO**

Em `PROGRESSO.md`, acrescentar após a seção da Fase 2a:

```markdown
## Fase 2b-1 — concluída

Variáveis: resolvedor de símbolos, grafo com tipo e escopo, 8 regras novas
(NM001–NM003, PF102–PF106). Plano:
`docs/superpowers/plans/2026-08-19-pp-lint-fase-2b1.md`.

O catálogo passou de 5 para 13 regras. A PF104 foi calibrada contra o
`chess-real.msapp` — 827 fórmulas reais — porque tem severidade Error e um falso
positivo quebraria o build de quem confia na ferramenta.

## Próximas fases

- **2b-2** — lógica redundante em expressões (PF111–PF118).
- **2b-3** — telas, componentes, fluxos, colunas (NM012–NM041, PF120–PF130).
- **2c** — formatos JSON e SARIF, `explain`, índice por artefato.
```

- [ ] **Step 4: Verificar o comportamento no app real**

```bash
cd /c/PROJETOS/pp-lint
dotnet run --project src/PpLint.Cli -c Release -- check tests/fixtures/chess-real.msapp --no-color --quiet
dotnet run --project src/PpLint.Cli -c Release -- check tests/fixtures/chess-real.msapp --no-color --select PF
```

Expected: o resumo mostra as regras novas; nenhuma exceção; a contagem de PF104 é a mesma verificada na Task 8. Leia os achados de PF102, PF103 e PF105 e confirme que fazem sentido para um app real — se algum parecer absurdo, o defeito é da regra, não do app.

- [ ] **Step 5: Rodar tudo e commitar**

```bash
dotnet test
git add -A
git commit -m "docs: catálogo de 13 regras e semântica de variáveis no README"
```

---

## Cobertura do spec nesta fase

Entregue: grafo de variáveis com globais, contexto (por tela) e coleções; resolução
de identificadores contra controles, telas, fontes de dados, funções, enums e escopos
de linha; regras NM001, NM002, NM003, PF102, PF103, PF104, PF105 e PF106.

Adiado, com a fase de destino:

| Item do spec | Fase |
|---|---|
| PF111–PF118 (lógica redundante) | 2b-2 |
| PF113, PF114 (ramos idênticos e inalcançáveis) | 2b-2 |
| NM012–NM014 (telas e componentes) | 2b-3 |
| NM020–NM023 (colunas de dados) | 2b-3 |
| NM030, NM031, NM040, NM041 | 2b-3 |
| PF120–PF130 (antipadrões estruturais) | 2b-3 |
| Catálogo FL completo | depende de uma solução exportada real |
| JSON, SARIF, `explain`, índice por artefato | 2c |

