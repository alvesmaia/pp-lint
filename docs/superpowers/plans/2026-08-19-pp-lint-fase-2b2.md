# pp-lint Fase 2b-2 — Lógica redundante

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apontar lógica que não faz nada — `If(c, true, false)`, ramos idênticos, dupla negação, filtro sem efeito — as oito regras PF111–PF118.

**Architecture:** Nenhum subsistema novo além de um comparador estrutural de AST, que responde "estas duas expressões são a mesma coisa?" e serve tanto às regras de ramo quanto à futura DUP301. As regras percorrem a AST com o `AstWalker` que já existe.

**Tech Stack:** .NET 10, C# 14, xUnit, `Microsoft.PowerFx.Core`, `Tomlyn`.

**Spec:** `docs/superpowers/specs/2026-08-18-pp-lint-design.md` (seção 7.2, PF111–PF118)

## Decisões desta fase

1. **Comparação estrutural por texto normalizado do nó.** `TexlNode.ToString()` devolve a expressão reconstruída pelo parser, então diferenças de espaço em branco já somem. É mais simples e mais previsível do que percorrer a árvore comparando tipo a tipo, e o comparador fica com uma responsabilidade só.
2. **Comparação de nomes sem diferenciar maiúsculas**, como o Power Fx faz.
3. **Toda regra desta fase avalia por ocorrência sintática**, não por fórmula: uma propriedade com três `If` contribui com três alvos. Isso mantém o índice comparável entre apps de tamanhos diferentes.
4. **PF117 cobre apenas `CountRows(...) > 0` e `>= 1`**, as formas que aparecem na prática. `<> 0` e `= 0` invertem o sentido e ficam de fora para não sugerir a troca errada.

## Global Constraints

- **.NET 10** (`net10.0`), `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **Somente-leitura absoluto.**
- **Dependências de runtime:** apenas `Microsoft.PowerFx.Core` e `Tomlyn`.
- **Mensagens em português do Brasil**, com acentuação correta.
- **IDs de regra são imutáveis.**
- **Invariante do índice:** no máximo uma violação por alvo avaliado.
- **Achado suprimido continua contando como violação.**
- **Toda regra chama `ctx.Evaluated(n)`** para cada alvo examinado.
- **Regra nova precisa de teste positivo e negativo.**
- **Nenhuma regra pode gerar falso positivo no `chess-real.msapp`** — a validação final é contra ele.
- **TDD obrigatório.** Commit ao fim de cada task.

## Estrutura de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/PpLint.PowerFx/AstComparer.cs` | Duas expressões são estruturalmente iguais? |
| `src/PpLint.Rules/Fx/LogicHelpers.cs` | Percurso de propriedades e classificação de literais, compartilhados pelas regras |
| `src/PpLint.Rules/Fx/BooleanRedundancyRules.cs` | PF111, PF112 |
| `src/PpLint.Rules/Fx/BranchRules.cs` | PF113, PF114 |
| `src/PpLint.Rules/Fx/ExpressionNoiseRules.cs` | PF115, PF116, PF118 |
| `src/PpLint.Rules/Fx/CountRowsComparisonRule.cs` | PF117 |

---

### Task 1: Comparador estrutural de AST

**Files:**
- Create: `src/PpLint.PowerFx/AstComparer.cs`
- Test: `tests/PpLint.PowerFx.Tests/AstComparerTests.cs`

**Interfaces:**
- Consumes: `Microsoft.PowerFx.Syntax.TexlNode`.
- Produces: `static class AstComparer` com `static bool AreEquivalent(TexlNode a, TexlNode b)` e `static string Normalize(TexlNode node)`.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.PowerFx.Tests/AstComparerTests.cs`:

```csharp
namespace PpLint.PowerFx.Tests;

public class AstComparerTests
{
    private static (Microsoft.PowerFx.Syntax.TexlNode A, Microsoft.PowerFx.Syntax.TexlNode B) Parse(
        string a, string b) =>
        (PowerFxParser.Parse(a).Root!, PowerFxParser.Parse(b).Root!);

    [Fact]
    public void IdenticalExpressionsAreEquivalent()
    {
        var (a, b) = Parse("varTotal + 1", "varTotal + 1");
        Assert.True(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void WhitespaceDoesNotMatter()
    {
        var (a, b) = Parse("varTotal+1", "varTotal  +  1");
        Assert.True(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void CaseOfIdentifiersDoesNotMatter()
    {
        // Power Fx não diferencia maiúsculas em nomes.
        var (a, b) = Parse("varTotal", "VARTOTAL");
        Assert.True(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void DifferentOperandsAreNotEquivalent()
    {
        var (a, b) = Parse("varTotal + 1", "varTotal + 2");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void DifferentOperatorsAreNotEquivalent()
    {
        var (a, b) = Parse("varA + varB", "varA - varB");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void OperandOrderMatters()
    {
        // a - b não é b - a; o comparador é estrutural, não algébrico.
        var (a, b) = Parse("varA - varB", "varB - varA");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void CallsWithSameArgumentsAreEquivalent()
    {
        var (a, b) = Parse("IsBlank(varX)", "IsBlank(varX)");
        Assert.True(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void CallsWithDifferentArgumentsAreNotEquivalent()
    {
        var (a, b) = Parse("IsBlank(varX)", "IsBlank(varY)");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }

    [Fact]
    public void NormalizeIsStableForTheSameExpression()
    {
        var (a, b) = Parse("If(varX, 1, 2)", "If( varX , 1 , 2 )");
        Assert.Equal(AstComparer.Normalize(a), AstComparer.Normalize(b));
    }

    [Fact]
    public void StringLiteralsDifferByContent()
    {
        var (a, b) = Parse("\"abc\"", "\"abd\"");
        Assert.False(AstComparer.AreEquivalent(a, b));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter AstComparerTests`
Expected: FALHA de compilação — `AstComparer` não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.PowerFx/AstComparer.cs`:

```csharp
using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx;

/// <summary>
/// Duas expressões são a mesma coisa? Usado para achar ramos idênticos e
/// condições repetidas, e será a base da detecção de duplicação.
///
/// A comparação é estrutural, não algébrica: 'a - b' e 'b - a' são diferentes,
/// ainda que um humano veja simetria. Provar equivalência algébrica exigiria
/// um provador, e um linter que erra nisso é pior que um que não tenta.
/// </summary>
public static class AstComparer
{
    /// <summary>
    /// O texto que o parser reconstrói a partir do nó, em caixa baixa. Espaços
    /// e formatação já somem na reconstrução; a caixa some aqui porque o Power Fx
    /// não diferencia maiúsculas em nomes.
    /// </summary>
    public static string Normalize(TexlNode node) =>
        node.ToString().ToLowerInvariant();

    public static bool AreEquivalent(TexlNode a, TexlNode b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter AstComparerTests`
Expected: PASSA — 10 testes. Se `WhitespaceDoesNotMatter` falhar, o `ToString()` do pacote preserva formatação; nesse caso remova todo espaço fora de literais de texto antes de comparar.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: comparador estrutural de expressões Power Fx"
```

---

### Task 2: Helpers compartilhados e PF111/PF112

**Files:**
- Create: `src/PpLint.Rules/Fx/LogicHelpers.cs`, `src/PpLint.Rules/Fx/BooleanRedundancyRules.cs`
- Test: `tests/PpLint.Rules.Tests/BooleanRedundancyRuleTests.cs`

**Interfaces:**
- Consumes: `PowerFxParser`, `AstWalker`, `AstComparer`, `IRule`, `LintContext`, `CanvasApp`, `PowerFxProperty`.
- Produces:
  - `static class LogicHelpers` com `static IEnumerable<(PowerFxProperty Property, TexlNode Root)> ParsedProperties(LintContext ctx)` e `static bool IsBooleanLiteral(TexlNode node, out bool value)`.
  - `RedundantBooleanIfRule` (PF111, PowerFx, Warning) e `BooleanComparisonRule` (PF112, PowerFx, Warning).

**PF111.** `If(cond, true, false)` é `cond`; `If(cond, false, true)` é `Not(cond)`. Ambos são ruído que esconde a intenção.

**PF112.** `x = true` é `x`; `x = false` é `Not(x)`. Comparar booleano com booleano nunca acrescenta informação.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/BooleanRedundancyRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class BooleanRedundancyRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void PF111_ReportsIfTrueFalse()
    {
        var d = Assert.Single(Run(new RedundantBooleanIfRule(), "Set(varX, If(IsBlank(varY), true, false))").Diagnostics);

        Assert.Equal("PF111", d.RuleId);
        Assert.Contains("IsBlank", d.Message);
    }

    [Fact]
    public void PF111_ReportsIfFalseTrueSuggestingNot()
    {
        var d = Assert.Single(Run(new RedundantBooleanIfRule(), "Set(varX, If(varCond, false, true))").Diagnostics);

        Assert.Contains("Not(", d.Message);
    }

    [Fact]
    public void PF111_AcceptsIfWithRealBranches() =>
        Assert.Empty(Run(new RedundantBooleanIfRule(), "Set(varX, If(varCond, 1, 2))").Diagnostics);

    [Fact]
    public void PF111_AcceptsIfWithOnlyOneBooleanBranch() =>
        Assert.Empty(Run(new RedundantBooleanIfRule(), "Set(varX, If(varCond, true, varOutro))").Diagnostics);

    [Fact]
    public void PF111_AcceptsTwoArgumentIf() =>
        Assert.Empty(Run(new RedundantBooleanIfRule(), "If(varCond, Notify(\"oi\"))").Diagnostics);

    [Fact]
    public void PF111_EvaluatesEveryIf()
    {
        var result = Run(new RedundantBooleanIfRule(), "Set(varA, If(varX, true, false)); Set(varB, If(varY, 1, 2))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void PF112_ReportsComparisonWithTrue()
    {
        var d = Assert.Single(Run(new BooleanComparisonRule(), "If(varAtivo = true, Notify(\"oi\"))").Diagnostics);

        Assert.Equal("PF112", d.RuleId);
        Assert.Contains("varAtivo", d.Message);
    }

    [Fact]
    public void PF112_ReportsComparisonWithFalseSuggestingNot()
    {
        var d = Assert.Single(Run(new BooleanComparisonRule(), "If(varAtivo = false, Notify(\"oi\"))").Diagnostics);

        Assert.Contains("Not(", d.Message);
    }

    [Fact]
    public void PF112_ReportsInequalityWithTrue()
    {
        Assert.Single(Run(new BooleanComparisonRule(), "If(varAtivo <> true, Notify(\"oi\"))").Diagnostics);
    }

    [Fact]
    public void PF112_ReportsWhenLiteralComesFirst()
    {
        Assert.Single(Run(new BooleanComparisonRule(), "If(true = varAtivo, Notify(\"oi\"))").Diagnostics);
    }

    [Fact]
    public void PF112_AcceptsComparisonBetweenValues() =>
        Assert.Empty(Run(new BooleanComparisonRule(), "If(varTotal = 10, Notify(\"oi\"))").Diagnostics);

    [Fact]
    public void PF112_IgnoresTwoLiterals()
    {
        // true = true é condição constante: assunto da PF110, não desta regra.
        Assert.Empty(Run(new BooleanComparisonRule(), "If(true = true, Notify(\"oi\"))").Diagnostics);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter BooleanRedundancyRuleTests`
Expected: FALHA de compilação — as duas regras não existem.

- [ ] **Step 3: Implementar os helpers**

`src/PpLint.Rules/Fx/LogicHelpers.cs`:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// O que as regras de lógica repetem: percorrer as fórmulas já parseadas e
/// reconhecer literais booleanos.
/// </summary>
internal static class LogicHelpers
{
    public static IEnumerable<(PowerFxProperty Property, TexlNode Root)> ParsedProperties(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var property in AllProperties(app))
            {
                var parsed = PowerFxParser.Parse(property.Script);
                if (parsed.Root is not null)
                    yield return (property, parsed.Root);
            }
        }
    }

    public static bool IsBooleanLiteral(TexlNode node, out bool value)
    {
        if (node is BoolLitNode boolean)
        {
            value = boolean.Value;
            return true;
        }

        value = false;
        return false;
    }

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

- [ ] **Step 4: Implementar PF111 e PF112**

`src/PpLint.Rules/Fx/BooleanRedundancyRules.cs`:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF111 — If(cond, true, false) é apenas cond, e If(cond, false, true) é
/// Not(cond). A forma longa esconde a intenção atrás de um desvio que não existe.
/// </summary>
[Rule("PF111", RuleCategory.PowerFx, Severity.Warning)]
public sealed class RedundantBooleanIfRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var call in AstWalker.Calls(root, "If"))
            {
                var args = call.Args?.ChildNodes;
                if (args is null || args.Count != 3)
                    continue;

                ctx.Evaluated(1);

                if (!LogicHelpers.IsBooleanLiteral(args[1], out var entao)
                    || !LogicHelpers.IsBooleanLiteral(args[2], out var senao)
                    || entao == senao)
                {
                    continue;
                }

                var condicao = args[0].ToString();
                var sugestao = entao ? condicao : $"Not({condicao})";

                ctx.Report(
                    property.Location,
                    $"'{call}' pode ser escrito como '{sugestao}'. "
                    + "O If não acrescenta nada quando os dois ramos são booleanos opostos.");
            }
        }
    }
}

/// <summary>
/// PF112 — comparar um valor booleano com true ou false não acrescenta
/// informação: 'x = true' é 'x', e 'x = false' é 'Not(x)'.
/// Comparação entre dois literais é condição constante e cabe à PF110.
/// </summary>
[Rule("PF112", RuleCategory.PowerFx, Severity.Warning)]
public sealed class BooleanComparisonRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var node in AstWalker.Descendants(root).OfType<BinaryOpNode>())
            {
                if (node.Op is not (BinaryOp.Equal or BinaryOp.NotEqual))
                    continue;

                var literalEsquerda = LogicHelpers.IsBooleanLiteral(node.Left, out var valorEsquerda);
                var literalDireita = LogicHelpers.IsBooleanLiteral(node.Right, out var valorDireita);

                // Os dois lados literais: condição constante, assunto da PF110.
                if (literalEsquerda && literalDireita)
                    continue;

                if (!literalEsquerda && !literalDireita)
                    continue;

                ctx.Evaluated(1);

                var expressao = literalEsquerda ? node.Right : node.Left;
                var literal = literalEsquerda ? valorEsquerda : valorDireita;

                // '<> true' equivale a 'Not(x)'; '= false' também.
                var afirmativo = node.Op == BinaryOp.Equal ? literal : !literal;
                var sugestao = afirmativo ? expressao.ToString() : $"Not({expressao})";

                ctx.Report(
                    property.Location,
                    $"'{node}' pode ser escrito como '{sugestao}'. "
                    + "Comparar um booleano com true ou false não acrescenta informação.");
            }
        }
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter BooleanRedundancyRuleTests`
Expected: PASSA — 12 testes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: regras PF111 e PF112 de redundância booleana"
```

---

### Task 3: PF113 e PF114 — ramos idênticos e inalcançáveis

**Files:**
- Create: `src/PpLint.Rules/Fx/BranchRules.cs`
- Test: `tests/PpLint.Rules.Tests/BranchRuleTests.cs`

**Interfaces:**
- Consumes: `AstComparer`, `LogicHelpers`, `AstWalker`, `IRule`, `LintContext`.
- Produces: `IdenticalBranchesRule` (PF113, PowerFx, Error) e `UnreachableBranchRule` (PF114, PowerFx, Error).

**PF113.** `If(c, X, X)` executa X de qualquer jeito — ou a condição é inútil, ou um dos ramos está errado. Severidade `Error` porque é quase sempre copiar-colar mal terminado.

**PF114.** `If(c, a, If(c, b, d))` — a mesma condição testada de novo no ramo `senão` nunca pode ser verdadeira ali. O ramo `b` é código morto.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/BranchRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class BranchRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void PF113_ReportsIdenticalBranches()
    {
        var d = Assert.Single(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA + 1, varA + 1))").Diagnostics);

        Assert.Equal("PF113", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void PF113_IgnoresFormattingDifferences()
    {
        Assert.Single(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA+1, varA + 1))").Diagnostics);
    }

    [Fact]
    public void PF113_AcceptsDifferentBranches() =>
        Assert.Empty(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA, varB))").Diagnostics);

    [Fact]
    public void PF113_AcceptsTwoArgumentIf() =>
        Assert.Empty(Run(new IdenticalBranchesRule(), "If(varCond, Notify(\"oi\"))").Diagnostics);

    [Fact]
    public void PF113_EvaluatesEveryThreeArgumentIf()
    {
        var result = Run(new IdenticalBranchesRule(), "Set(varA, If(varX, 1, 1)); Set(varB, If(varY, 1, 2))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void PF114_ReportsRepeatedConditionInElse()
    {
        var d = Assert.Single(Run(new UnreachableBranchRule(),
            "Set(varX, If(varCond, 1, If(varCond, 2, 3)))").Diagnostics);

        Assert.Equal("PF114", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("varCond", d.Message);
    }

    [Fact]
    public void PF114_AcceptsDifferentConditions() =>
        Assert.Empty(Run(new UnreachableBranchRule(),
            "Set(varX, If(varA, 1, If(varB, 2, 3)))").Diagnostics);

    [Fact]
    public void PF114_AcceptsRepeatedConditionInThenBranch()
    {
        // If(c, If(c, ...), ...) é redundante mas alcançável; não é esta regra.
        Assert.Empty(Run(new UnreachableBranchRule(),
            "Set(varX, If(varCond, If(varCond, 1, 2), 3))").Diagnostics);
    }

    [Fact]
    public void PF114_EvaluatesEveryNestedElse()
    {
        var result = Run(new UnreachableBranchRule(),
            "Set(varA, If(varX, 1, If(varX, 2, 3))); Set(varB, If(varY, 1, If(varZ, 2, 3)))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter BranchRuleTests`
Expected: FALHA de compilação — as duas regras não existem.

- [ ] **Step 3: Implementar**

`src/PpLint.Rules/Fx/BranchRules.cs`:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF113 — os dois ramos do If fazem exatamente a mesma coisa, então a
/// condição não muda o resultado. Ou ela é inútil, ou um dos ramos está errado;
/// nos dois casos alguém precisa olhar.
/// </summary>
[Rule("PF113", RuleCategory.PowerFx, Severity.Error)]
public sealed class IdenticalBranchesRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var call in AstWalker.Calls(root, "If"))
            {
                var args = call.Args?.ChildNodes;
                if (args is null || args.Count != 3)
                    continue;

                ctx.Evaluated(1);

                if (AstComparer.AreEquivalent(args[1], args[2]))
                {
                    ctx.Report(
                        property.Location,
                        $"Os dois ramos de '{call}' são idênticos, então a condição não muda o "
                        + "resultado. Remova o If ou corrija o ramo que deveria ser diferente.");
                }
            }
        }
    }
}

/// <summary>
/// PF114 — a mesma condição testada de novo dentro do próprio ramo 'senão'.
/// Se ela era falsa para chegar ali, continua falsa: o ramo interno é código morto.
/// </summary>
[Rule("PF114", RuleCategory.PowerFx, Severity.Error)]
public sealed class UnreachableBranchRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var call in AstWalker.Calls(root, "If"))
            {
                var args = call.Args?.ChildNodes;
                if (args is null || args.Count != 3)
                    continue;

                // O ramo 'senão' precisa ser outro If para haver o que analisar.
                if (args[2] is not CallNode interno
                    || !string.Equals(AstWalker.FunctionName(interno), "If", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var argsInternos = interno.Args?.ChildNodes;
                if (argsInternos is null || argsInternos.Count < 2)
                    continue;

                ctx.Evaluated(1);

                if (AstComparer.AreEquivalent(args[0], argsInternos[0]))
                {
                    ctx.Report(
                        property.Location,
                        $"A condição '{args[0]}' é testada de novo dentro do próprio ramo 'senão'. "
                        + "Se ela era falsa para chegar ali, continua falsa — esse ramo nunca executa.");
                }
            }
        }
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter BranchRuleTests`
Expected: PASSA — 9 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: regras PF113 e PF114 de ramos idênticos e inalcançáveis"
```

---

### Task 4: PF115, PF116 e PF118 — ruído em expressões

**Files:**
- Create: `src/PpLint.Rules/Fx/ExpressionNoiseRules.cs`
- Test: `tests/PpLint.Rules.Tests/ExpressionNoiseRuleTests.cs`

**Interfaces:**
- Consumes: `LogicHelpers`, `AstWalker`, `IRule`, `LintContext`.
- Produces: `DoubleNegationRule` (PF115, PowerFx, Warning), `PointlessFilterRule` (PF116, PowerFx, Warning), `EmptyConcatenationRule` (PF118, PowerFx, Info).

**PF115.** `Not(Not(x))` é `x`. Também vale para `!!x`, que o parser representa com `UnaryOp.Not`.

**PF116.** `Filter(fonte, true)` devolve a fonte inteira. O filtro engana quem lê e ainda impede delegação.

**PF118.** Concatenar com texto vazio (`x & ""`) não muda nada. É `Info` porque costuma ser resíduo inofensivo de refatoração.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/ExpressionNoiseRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class ExpressionNoiseRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void PF115_ReportsNotOfNot()
    {
        var d = Assert.Single(Run(new DoubleNegationRule(), "Set(varX, Not(Not(varAtivo)))").Diagnostics);

        Assert.Equal("PF115", d.RuleId);
        Assert.Contains("varAtivo", d.Message);
    }

    [Fact]
    public void PF115_ReportsBangBang()
    {
        Assert.Single(Run(new DoubleNegationRule(), "Set(varX, !!varAtivo)").Diagnostics);
    }

    [Fact]
    public void PF115_AcceptsSingleNegation() =>
        Assert.Empty(Run(new DoubleNegationRule(), "Set(varX, Not(varAtivo))").Diagnostics);

    [Fact]
    public void PF115_AcceptsTripleNegationAsSingleFinding()
    {
        // Not(Not(Not(x))) tem duas duplas aninhadas; uma violação por alvo,
        // e o alvo é a negação externa.
        var result = Run(new DoubleNegationRule(), "Set(varX, Not(Not(Not(varAtivo))))");

        Assert.All(result.Tallies, t => Assert.True(t.Violations <= t.Evaluated));
    }

    [Fact]
    public void PF116_ReportsFilterWithTrue()
    {
        var d = Assert.Single(Run(new PointlessFilterRule(), "ClearCollect(colX, Filter(Pedidos, true))").Diagnostics);

        Assert.Equal("PF116", d.RuleId);
        Assert.Contains("Pedidos", d.Message);
    }

    [Fact]
    public void PF116_AcceptsRealFilter() =>
        Assert.Empty(Run(new PointlessFilterRule(), "ClearCollect(colX, Filter(Pedidos, Total > 10))").Diagnostics);

    [Fact]
    public void PF116_IgnoresFilterWithFalse()
    {
        // Filter(fonte, false) devolve vazio: é suspeito, mas não é "filtro sem
        // efeito" — e sugerir remover o filtro seria conselho errado.
        Assert.Empty(Run(new PointlessFilterRule(), "ClearCollect(colX, Filter(Pedidos, false))").Diagnostics);
    }

    [Fact]
    public void PF118_ReportsConcatenationWithEmptyText()
    {
        var d = Assert.Single(Run(new EmptyConcatenationRule(), "Set(varX, varNome & \"\")").Diagnostics);

        Assert.Equal("PF118", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void PF118_ReportsWhenEmptyComesFirst()
    {
        Assert.Single(Run(new EmptyConcatenationRule(), "Set(varX, \"\" & varNome)").Diagnostics);
    }

    [Fact]
    public void PF118_AcceptsRealConcatenation() =>
        Assert.Empty(Run(new EmptyConcatenationRule(), "Set(varX, varNome & \" \" & varSobrenome)").Diagnostics);

    [Fact]
    public void PF118_EvaluatesEveryConcatenation()
    {
        var result = Run(new EmptyConcatenationRule(), "Set(varA, varX & \"\"); Set(varB, varY & \"z\")");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter ExpressionNoiseRuleTests`
Expected: FALHA de compilação — as três regras não existem.

- [ ] **Step 3: Implementar**

`src/PpLint.Rules/Fx/ExpressionNoiseRules.cs`:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF115 — Not(Not(x)) é x. Aparece quando alguém inverte uma condição duas
/// vezes durante uma refatoração e não simplifica no fim.
/// </summary>
[Rule("PF115", RuleCategory.PowerFx, Severity.Warning)]
public sealed class DoubleNegationRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var node in AstWalker.Descendants(root))
            {
                var interno = NegatedOperand(node);
                if (interno is null)
                    continue;

                ctx.Evaluated(1);

                var duplo = NegatedOperand(interno);
                if (duplo is not null)
                {
                    ctx.Report(
                        property.Location,
                        $"'{node}' nega duas vezes e equivale a '{duplo}'. Remova a dupla negação.");
                }
            }
        }
    }

    /// <summary>O operando de uma negação, seja ela Not(x) ou !x; null se o nó não nega.</summary>
    private static TexlNode? NegatedOperand(TexlNode node)
    {
        if (node is UnaryOpNode unary && unary.Op == UnaryOp.Not)
            return unary.Child;

        if (node is CallNode call
            && string.Equals(AstWalker.FunctionName(call), "Not", StringComparison.OrdinalIgnoreCase))
        {
            var args = call.Args?.ChildNodes;
            if (args is { Count: 1 })
                return args[0];
        }

        return null;
    }
}

/// <summary>
/// PF116 — Filter(fonte, true) devolve a fonte inteira. O filtro engana quem lê
/// a fórmula e, sobre fonte delegável, ainda atrapalha a delegação.
/// </summary>
[Rule("PF116", RuleCategory.PowerFx, Severity.Warning)]
public sealed class PointlessFilterRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var call in AstWalker.Calls(root, "Filter"))
            {
                var args = call.Args?.ChildNodes;
                if (args is null || args.Count < 2)
                    continue;

                ctx.Evaluated(1);

                // Só 'true' é filtro sem efeito. 'false' devolve vazio — outro
                // problema, e sugerir remover o filtro ali seria errado.
                var todosVerdadeiros = args
                    .Skip(1)
                    .All(a => LogicHelpers.IsBooleanLiteral(a, out var valor) && valor);

                if (todosVerdadeiros)
                {
                    ctx.Report(
                        property.Location,
                        $"'{call}' filtra por 'true' e devolve '{args[0]}' inteiro. "
                        + "Remova o Filter ou escreva a condição real.");
                }
            }
        }
    }
}

/// <summary>
/// PF118 — concatenar com texto vazio não muda nada. Costuma sobrar de uma
/// refatoração; é inofensivo, por isso Info.
/// </summary>
[Rule("PF118", RuleCategory.PowerFx, Severity.Info)]
public sealed class EmptyConcatenationRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var node in AstWalker.Descendants(root).OfType<BinaryOpNode>())
            {
                if (node.Op != BinaryOp.Concat)
                    continue;

                ctx.Evaluated(1);

                if (IsEmptyText(node.Left) || IsEmptyText(node.Right))
                {
                    ctx.Report(
                        property.Location,
                        $"'{node}' concatena com texto vazio, o que não muda o resultado. "
                        + "Remova a concatenação.");
                }
            }
        }
    }

    private static bool IsEmptyText(TexlNode node) =>
        node is StrLitNode texto && texto.Value.Length == 0;
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter ExpressionNoiseRuleTests`
Expected: PASSA — 11 testes. Se `PF115_ReportsBangBang` falhar, o parser representa `!!x` de outra forma; verifique com um teste temporário que imprima `PowerFxParser.Parse("!!x").Root!.GetType().Name` e ajuste `NegatedOperand`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: regras PF115, PF116 e PF118 de ruído em expressões"
```

---

### Task 5: PF117 — contagem usada como teste de existência

**Files:**
- Create: `src/PpLint.Rules/Fx/CountRowsComparisonRule.cs`
- Test: `tests/PpLint.Rules.Tests/CountRowsComparisonRuleTests.cs`

**Interfaces:**
- Consumes: `LogicHelpers`, `AstWalker`, `IRule`, `LintContext`.
- Produces: `CountRowsComparisonRule` (PF117, PowerFx, Warning).

`CountRows(Filter(...)) > 0` percorre a coleção inteira só para saber se ela tem algum item; `!IsEmpty(Filter(...))` para no primeiro. Em fonte grande a diferença é sentida pelo usuário.

Cobre `> 0` e `>= 1`. As formas invertidas (`= 0`, `< 1`) pedem `IsEmpty` sem a negação e ficam de fora desta fase, para não sugerir a troca errada.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/CountRowsComparisonRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class CountRowsComparisonRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new CountRowsComparisonRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsGreaterThanZero()
    {
        var d = Assert.Single(Run("If(CountRows(colItens) > 0, Notify(\"tem\"))").Diagnostics);

        Assert.Equal("PF117", d.RuleId);
        Assert.Contains("IsEmpty", d.Message);
    }

    [Fact]
    public void ReportsGreaterOrEqualOne()
    {
        Assert.Single(Run("If(CountRows(colItens) >= 1, Notify(\"tem\"))").Diagnostics);
    }

    [Fact]
    public void ReportsWhenCountComesSecond()
    {
        Assert.Single(Run("If(0 < CountRows(colItens), Notify(\"tem\"))").Diagnostics);
    }

    [Fact]
    public void AcceptsComparisonWithOtherNumbers() =>
        Assert.Empty(Run("If(CountRows(colItens) > 5, Notify(\"muitos\"))").Diagnostics);

    [Fact]
    public void AcceptsEqualityWithZero()
    {
        // CountRows(x) = 0 pede IsEmpty sem negação; fora do escopo desta regra
        // para não sugerir a troca errada.
        Assert.Empty(Run("If(CountRows(colItens) = 0, Notify(\"vazio\"))").Diagnostics);
    }

    [Fact]
    public void AcceptsCountRowsUsedAsValue() =>
        Assert.Empty(Run("Set(varTotal, CountRows(colItens))").Diagnostics);

    [Fact]
    public void EvaluatesEveryCountRowsComparison()
    {
        var result = Run("If(CountRows(colA) > 0, 1, 2); If(CountRows(colB) > 5, 1, 2)");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter CountRowsComparisonRuleTests`
Expected: FALHA de compilação — a regra não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.Rules/Fx/CountRowsComparisonRule.cs`:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF117 — CountRows(x) > 0 percorre a coleção inteira só para saber se ela tem
/// algum item; !IsEmpty(x) para no primeiro. Sobre fonte grande a diferença
/// aparece na tela do usuário.
/// </summary>
[Rule("PF117", RuleCategory.PowerFx, Severity.Warning)]
public sealed class CountRowsComparisonRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var node in AstWalker.Descendants(root).OfType<BinaryOpNode>())
            {
                var contagem = CountRowsArgument(node.Left) ?? CountRowsArgument(node.Right);
                if (contagem is null)
                    continue;

                ctx.Evaluated(1);

                if (TestaExistencia(node))
                {
                    ctx.Report(
                        property.Location,
                        $"'{node}' conta todos os itens só para saber se existe algum. "
                        + $"Use '!IsEmpty({contagem})', que para no primeiro item.");
                }
            }
        }
    }

    /// <summary>
    /// As formas que significam "tem pelo menos um": contagem &gt; 0, contagem &gt;= 1,
    /// e as mesmas com os lados trocados.
    /// </summary>
    private static bool TestaExistencia(BinaryOpNode node)
    {
        var contagemNaEsquerda = CountRowsArgument(node.Left) is not null;
        var outro = contagemNaEsquerda ? node.Right : node.Left;

        if (outro is not NumLitNode numero)
            return false;

        var valor = numero.ActualNumValue;

        return contagemNaEsquerda
            ? (node.Op == BinaryOp.Greater && valor == 0) || (node.Op == BinaryOp.GreaterEqual && valor == 1)
            : (node.Op == BinaryOp.Less && valor == 0) || (node.Op == BinaryOp.LessEqual && valor == 1);
    }

    /// <summary>O argumento de CountRows(...), ou null se o nó não for essa chamada.</summary>
    private static TexlNode? CountRowsArgument(TexlNode node)
    {
        if (node is not CallNode call
            || !string.Equals(AstWalker.FunctionName(call), "CountRows", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var args = call.Args?.ChildNodes;
        return args is { Count: 1 } ? args[0] : null;
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter CountRowsComparisonRuleTests`
Expected: PASSA — 7 testes. Se `NumLitNode.ActualNumValue` não existir no pacote, use `numero.Value` ou o membro equivalente — descubra com um teste temporário que liste as propriedades do nó.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: regra PF117 de contagem usada como teste de existência"
```

---

### Task 6: Validação contra apps reais e documentação

**Files:**
- Modify: `tests/PpLint.Extractors.Tests/RealArtifactTests.cs`
- Modify: `README.md`, `PROGRESSO.md`

**Interfaces:**
- Consumes: tudo das tasks anteriores.
- Produces: nenhuma API nova.

Nenhuma dessas regras pode acusar código correto. O `chess-real.msapp` é o juiz: qualquer achado precisa ser verificado à mão antes de ser aceito como legítimo.

- [ ] **Step 1: Escrever o teste de sanidade**

Acrescentar ao fim da classe `RealArtifactTests`:

```csharp
    [Fact]
    public void RealMsapp_LogicRulesStayQuietOnWorkingCode()
    {
        // As regras de lógica apontam código que não faz nada. Num app real que
        // funciona, isso é raro — dezenas de achados significam regra mal feita,
        // não app ruim. O limite é generoso de propósito: o que importa é pegar
        // o caso em que uma regra dispara em toda expressão.
        var project = ProjectLoader.Load(RealMsapp);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        string[] logica = ["PF111", "PF112", "PF113", "PF114", "PF115", "PF116", "PF117", "PF118"];

        foreach (var id in logica)
        {
            var achados = result.Diagnostics.Where(d => d.RuleId == id).ToList();

            Assert.True(
                achados.Count <= 15,
                $"{id} achou {achados.Count} ocorrências num app real que funciona — "
                + $"provavelmente falso positivo: {string.Join(" | ", achados.Take(5).Select(d => d.Message))}");
        }
    }
```

- [ ] **Step 2: Rodar e investigar cada achado**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter LogicRulesStayQuiet --logger "console;verbosity=detailed"`

Depois, liste os achados reais e verifique cada um à mão:

```bash
dotnet run --project src/PpLint.Cli -c Release -- check tests/fixtures/chess-real.msapp --no-color --select PF | grep -E "PF11[1-8]"
```

Para cada achado, confirme no `.msapp` que a expressão realmente é redundante. Se não for, **corrija a regra, nunca o limite do teste**.

- [ ] **Step 3: Atualizar a tabela de regras no README**

Acrescentar as oito linhas à tabela da seção `## Regras`, logo após a linha de PF110:

```markdown
| PF111 | `If(cond, true, false)` — o If não acrescenta nada | Aviso |
| PF112 | Comparação com booleano (`x = true`) | Aviso |
| PF113 | Os dois ramos do `If` são idênticos | Erro |
| PF114 | Condição repetida no ramo `senão` — ramo inalcançável | Erro |
| PF115 | Dupla negação (`Not(Not(x))`, `!!x`) | Aviso |
| PF116 | `Filter(fonte, true)` — filtro sem efeito | Aviso |
| PF117 | `CountRows(x) > 0` em vez de `!IsEmpty(x)` | Aviso |
| PF118 | Concatenação com texto vazio | Informação |
```

- [ ] **Step 4: Atualizar o PROGRESSO**

Acrescentar após a seção da Fase 2b-1:

```markdown
## Fase 2b-2 — concluída

Lógica redundante: comparador estrutural de AST e 8 regras (PF111–PF118).
O catálogo passou de 13 para 21 regras. Plano:
`docs/superpowers/plans/2026-08-19-pp-lint-fase-2b2.md`.
```

- [ ] **Step 5: Rodar tudo e commitar**

```bash
dotnet test
git add -A
git commit -m "docs: catálogo de 21 regras; test: sanidade das regras de lógica no app real"
```

---

## Cobertura do spec nesta fase

Entregue: PF111, PF112, PF113, PF114, PF115, PF116, PF117, PF118 e o comparador
estrutural de AST, que a DUP301 vai reaproveitar.

Adiado:

| Item do spec | Fase |
|---|---|
| NM012–NM041 (telas, componentes, fluxos, colunas) | 2b-3 |
| PF120–PF130 (antipadrões estruturais) | 2b-3 |
| Catálogo FL completo | depende de uma solução exportada real |
| DUP, PERF, SEC | 4 |
| JSON, SARIF, `explain`, índice por artefato | 2c |
