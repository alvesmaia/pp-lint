# pp-lint Fase 2c — Saídas de máquina e documentação de regras

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer o CLI cumprir o que promete — `--format json`, `--format sarif`, `explain <ID>` e índice de conformidade por artefato — e parar de anunciar o que não entrega.

**Architecture:** O resultado da análise deixa de ser uma lista achatada de diagnósticos e vira `AnalysisRun`, com um `ArtifactAnalysis` por caminho analisado e seu próprio índice. Os três reporters (texto, JSON, SARIF) consomem esse mesmo objeto. A documentação de cada regra vira um markdown embarcado no binário, que serve tanto ao `explain` quanto ao campo `help` do SARIF.

**Tech Stack:** .NET 10, C# 14, xUnit, `System.Text.Json` (`Utf8JsonWriter`), `Microsoft.PowerFx.Core`, `Tomlyn`.

**Spec:** `docs/superpowers/specs/2026-08-18-pp-lint-design.md` (seções 9 e 14, Fase 2)

## Por que agora

O `--help` anuncia cinco formatos e o comando `explain`. Nenhum funciona: `--format json`
responde "será entregue na Fase 2c" e sai com código 2. Um CLI que promete e não cumpre é
pior que um CLI menor — quem monta um pipeline em cima descobre o buraco em produção.

Esta fase fecha essa dívida e para de prometer HTML e Markdown, que pertencem à Fase 5.

## Decisões desta fase

1. **`Utf8JsonWriter`, não serialização por reflexão.** O JSON é contrato com quem
   automatiza em cima dele. Escrevê-lo campo a campo torna o formato explícito no código e
   impede que renomear uma propriedade C# quebre o consumidor em silêncio.
2. **`schemaVersion` no JSON desde a primeira versão.** Sem ele, a primeira mudança de
   formato não tem como ser detectada por quem consome.
3. **SARIF sem `region` quando não há linha.** O `.msapp` guarda fórmulas dentro de JSON
   gerado pelo Studio e o extractor não registra offset, então `Line` é 0. SARIF exige
   `startLine >= 1`: inventar linha 1 apontaria a anotação para o lugar errado. Omitimos a
   região e levamos entrada e símbolo em `logicalLocations`, que é o campo desenhado para
   isto. A anotação aparece no arquivo, não numa linha errada.
4. **`helpUri` aponta para o repositório público**, no caminho `docs/rules/<ID>.md`. O
   arquivo embarcado e o publicado são o mesmo conteúdo.
5. **HTML e Markdown saem do `--help` e do parser.** Voltam na Fase 5, junto com o
   relatório HTML que o spec já prevê.
6. **Índice por artefato só aparece no texto quando há mais de um artefato.** Repetir o
   índice geral logo abaixo dele, idêntico, é ruído.

## Global Constraints

- **.NET 10**, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **Somente-leitura absoluto** sobre o artefato analisado.
- **Dependências de runtime:** apenas `Microsoft.PowerFx.Core` e `Tomlyn`. `System.Text.Json` faz parte do runtime e não conta como dependência nova.
- **Mensagens em português do Brasil**, com acentuação correta.
- **IDs de regra são imutáveis.**
- **Invariante do índice:** no máximo uma violação por alvo avaliado.
- **TDD obrigatório.** Teste antes da implementação, commit ao fim de cada task, suíte inteira verde antes de cada commit.
- **Validação final contra os dois artefatos reais** (`tests/fixtures/chess-real.msapp` e `tests/fixtures/solucao-exemplo.zip`).

## Estrutura de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/PpLint.Core/Reporting/AnalysisRun.cs` | Resultado por artefato e agregado, com índice de cada um |
| `src/PpLint.Rules/Docs/*.md` | 28 documentos de regra, embarcados como recurso |
| `src/PpLint.Rules/RuleDocs.cs` | Lê e interpreta os documentos embarcados |
| `src/PpLint.Rules/PpLint.Rules.csproj` | *(modificar)* declara `Docs/*.md` como `EmbeddedResource` |
| `src/PpLint.Cli/JsonReporter.cs` | Serializa `AnalysisRun` no esquema versionado |
| `src/PpLint.Cli/SarifReporter.cs` | Serializa `AnalysisRun` em SARIF 2.1.0 |
| `src/PpLint.Cli/TextReporter.cs` | *(modificar)* índice por artefato |
| `src/PpLint.Cli/Program.cs` | *(modificar)* `explain`, roteamento de formato, `--output` |
| `src/PpLint.Cli/ArgumentParser.cs` | *(modificar)* remove `html` e `md` dos formatos válidos |

---

### Task 1: Resultado por artefato

**Files:**
- Create: `src/PpLint.Core/Reporting/AnalysisRun.cs`
- Test: `tests/PpLint.Core.Tests/AnalysisRunTests.cs`

**Interfaces:**
- Consumes: `Diagnostic`, `RuleTally`, `ComplianceScorer.Compute(IReadOnlyList<RuleTally>)`, `ComplianceReport`.
- Produces:
  - `sealed record ArtifactAnalysis(string Path, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<RuleTally> Tallies, ComplianceReport Compliance)`
  - `sealed record AnalysisRun(IReadOnlyList<ArtifactAnalysis> Artifacts, ComplianceReport Compliance, TimeSpan Elapsed)` com `IReadOnlyList<Diagnostic> AllDiagnostics`, `IReadOnlyList<RuleTally> AllTallies` e `static AnalysisRun From(IEnumerable<(string Path, LintResult Result)> results, TimeSpan elapsed)`

Hoje o `Program` concatena os diagnósticos de todos os caminhos numa lista só e calcula um
índice global. Isso impede dizer "a solução A está em 96% e a B em 71%", que é a pergunta
que alguém com várias soluções faz. O agregado continua existindo, calculado sobre todos os
tallies juntos — não é a média das partes, porque artefatos têm tamanhos diferentes e a
média enganaria.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/AnalysisRunTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;

namespace PpLint.Core.Tests;

public class AnalysisRunTests
{
    private static SourceLocation Loc(string artifact) => new(artifact, "e.json", "S", 0, 0);

    private static Diagnostic Diag(string artifact, string ruleId, Severity severity) =>
        new(ruleId, RuleCategory.Naming, severity, "mensagem", Loc(artifact));

    private static LintResult Result(string artifact, int evaluated, int violations)
    {
        var diagnostics = Enumerable.Range(0, violations)
            .Select(_ => Diag(artifact, "NM010", Severity.Warning))
            .ToList();

        return new LintResult(
            diagnostics,
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, evaluated, violations)]);
    }

    [Fact]
    public void EachArtifactKeepsItsOwnScore()
    {
        var run = AnalysisRun.From(
            [("a.msapp", Result("a.msapp", 10, 1)), ("b.msapp", Result("b.msapp", 10, 9))],
            TimeSpan.FromSeconds(1));

        Assert.Equal(2, run.Artifacts.Count);
        Assert.Equal(90.0, run.Artifacts[0].Compliance.Overall.Percent, 1);
        Assert.Equal(10.0, run.Artifacts[1].Compliance.Overall.Percent, 1);
    }

    [Fact]
    public void OverallIsComputedFromAllTalliesNotFromTheAverageOfArtifacts()
    {
        // Um artefato pequeno e ruim não pode pesar tanto quanto um grande e bom:
        // a média das partes daria 50%, mas 1 de 110 alvos falhou.
        var run = AnalysisRun.From(
            [("grande.msapp", Result("grande.msapp", 100, 0)), ("pequeno.msapp", Result("pequeno.msapp", 10, 1))],
            TimeSpan.Zero);

        Assert.Equal(110, run.Compliance.Overall.EvaluatedTargets);
        Assert.Equal(1, run.Compliance.Overall.Violations);
        Assert.True(
            run.Compliance.Overall.Percent > 99.0,
            $"esperava perto de 100%, veio {run.Compliance.Overall.Percent}");
    }

    [Fact]
    public void AllDiagnosticsPreservesArtifactOrder()
    {
        var run = AnalysisRun.From(
            [("a.msapp", Result("a.msapp", 1, 1)), ("b.msapp", Result("b.msapp", 1, 1))],
            TimeSpan.Zero);

        Assert.Equal(
            ["a.msapp", "b.msapp"],
            run.AllDiagnostics.Select(d => d.Location.ArtifactPath));
    }

    [Fact]
    public void EmptyRunIsFullyCompliant()
    {
        // Sem alvo avaliado não há como estar não-conforme; 0% assustaria à toa.
        var run = AnalysisRun.From([], TimeSpan.Zero);

        Assert.Empty(run.Artifacts);
        Assert.Equal(100.0, run.Compliance.Overall.Percent, 1);
    }

    [Fact]
    public void ElapsedIsCarried()
    {
        var run = AnalysisRun.From([], TimeSpan.FromMilliseconds(250));

        Assert.Equal(250, run.Elapsed.TotalMilliseconds, 1);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter AnalysisRunTests`
Expected: FALHA de compilação — `PpLint.Core.Reporting` e `AnalysisRun` não existem.

- [ ] **Step 3: Implementar**

`src/PpLint.Core/Reporting/AnalysisRun.cs`:

```csharp
using PpLint.Core.Rules;
using PpLint.Core.Scoring;

namespace PpLint.Core.Reporting;

/// <summary>O que a análise encontrou num artefato, com o índice dele.</summary>
public sealed record ArtifactAnalysis(
    string Path,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<RuleTally> Tallies,
    ComplianceReport Compliance);

/// <summary>
/// Uma execução inteira do linter: um resultado por artefato analisado, mais o
/// agregado.
///
/// O agregado é calculado sobre todos os tallies juntos, e não pela média dos
/// artefatos. Artefatos têm tamanhos muito diferentes — uma solução com três
/// telas e outra com trezentas — e a média das porcentagens daria à pequena o
/// mesmo peso da grande.
/// </summary>
public sealed record AnalysisRun(
    IReadOnlyList<ArtifactAnalysis> Artifacts,
    ComplianceReport Compliance,
    TimeSpan Elapsed)
{
    /// <summary>Todos os achados, na ordem em que os artefatos foram analisados.</summary>
    public IReadOnlyList<Diagnostic> AllDiagnostics { get; } =
        Artifacts.SelectMany(a => a.Diagnostics).ToList();

    public IReadOnlyList<RuleTally> AllTallies { get; } =
        Artifacts.SelectMany(a => a.Tallies).ToList();

    public static AnalysisRun From(
        IEnumerable<(string Path, LintResult Result)> results, TimeSpan elapsed)
    {
        var artifacts = results
            .Select(r => new ArtifactAnalysis(
                r.Path,
                r.Result.Diagnostics,
                r.Result.Tallies,
                ComplianceScorer.Compute(r.Result.Tallies)))
            .ToList();

        var todos = artifacts.SelectMany(a => a.Tallies).ToList();

        return new AnalysisRun(artifacts, ComplianceScorer.Compute(todos), elapsed);
    }
}
```

- [ ] **Step 4: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — 5 testes novos, nenhuma regressão. Nada consome `AnalysisRun` ainda.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: resultado de análise por artefato, com índice de cada um"
```

---

### Task 2: Índice por artefato no relatório de texto

**Files:**
- Modify: `src/PpLint.Cli/TextReporter.cs`
- Modify: `src/PpLint.Cli/Program.cs`
- Test: `tests/PpLint.Cli.Tests/TextReporterTests.cs` (acrescentar classe ao fim)

**Interfaces:**
- Consumes: `AnalysisRun`, `ArtifactAnalysis` da Task 1.
- Produces: `TextReporter.Render(AnalysisRun run, bool useColor, bool quiet)` — a assinatura antiga, que recebia diagnósticos, `ComplianceReport` e `TimeSpan` soltos, deixa de existir.

O `Program` passa a montar `AnalysisRun` e entregá-lo ao reporter, em vez de concatenar
listas. Com um artefato só, a saída não muda em nada — é o caso comum e não quero mexer
nele. Com dois ou mais, cada um ganha sua linha de índice antes do geral.

- [ ] **Step 1: Escrever o teste que falha**

Acrescentar ao fim de `tests/PpLint.Cli.Tests/TextReporterTests.cs`:

```csharp
public class PerArtifactComplianceTests
{
    private static SourceLocation Loc(string artifact) => new(artifact, "e.json", "S", 0, 0);

    private static LintResult Result(string artifact, int evaluated, int violations)
    {
        var diagnostics = Enumerable.Range(0, violations)
            .Select(_ => new Diagnostic(
                "NM010", RuleCategory.Naming, Severity.Warning, "nome padrão", Loc(artifact)))
            .ToList();

        return new LintResult(
            diagnostics,
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, evaluated, violations)]);
    }

    private static string Render(params (string Path, LintResult Result)[] results) =>
        TextReporter.Render(AnalysisRun.From(results, TimeSpan.FromSeconds(1)), useColor: false, quiet: true);

    [Fact]
    public void SingleArtifactShowsOnlyTheOverallScore()
    {
        // Com um artefato só, o índice dele e o geral são o mesmo número.
        // Imprimir os dois seria repetir a mesma informação em duas linhas.
        var texto = Render(("a.msapp", Result("a.msapp", 10, 1)));

        Assert.Contains("Conformidade geral", texto);
        Assert.DoesNotContain("Por artefato", texto);
    }

    [Fact]
    public void SeveralArtifactsGetOneLineEach()
    {
        var texto = Render(
            ("a.msapp", Result("a.msapp", 10, 1)),
            ("b.msapp", Result("b.msapp", 10, 9)));

        Assert.Contains("Por artefato", texto);
        Assert.Contains("a.msapp", texto);
        Assert.Contains("b.msapp", texto);
        Assert.Contains("90,0%", texto);
        Assert.Contains("10,0%", texto);
    }

    [Fact]
    public void OverallStillAppearsWithSeveralArtifacts()
    {
        var texto = Render(
            ("a.msapp", Result("a.msapp", 10, 1)),
            ("b.msapp", Result("b.msapp", 10, 9)));

        Assert.Contains("Conformidade geral", texto);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Cli.Tests --filter PerArtifactComplianceTests`
Expected: FALHA de compilação — `TextReporter.Render` ainda tem a assinatura antiga.

- [ ] **Step 3: Trocar a assinatura do reporter**

Em `src/PpLint.Cli/TextReporter.cs`, substituir o método `Render` por:

```csharp
    public static string Render(AnalysisRun run, bool useColor, bool quiet)
    {
        var sb = new StringBuilder();

        if (!quiet)
            RenderDiagnostics(sb, run.AllDiagnostics, useColor);

        RenderPerArtifact(sb, run, useColor);
        RenderCompliance(sb, run.Compliance, useColor);
        RenderSummary(sb, run.AllDiagnostics, run.Elapsed);

        return sb.ToString();
    }

    /// <summary>
    /// Uma linha por artefato, só quando há mais de um. Com um artefato só, este
    /// número é idêntico ao geral logo abaixo, e repeti-lo é ruído.
    /// </summary>
    private static void RenderPerArtifact(StringBuilder sb, AnalysisRun run, bool color)
    {
        if (run.Artifacts.Count < 2)
            return;

        sb.AppendLine();
        sb.AppendLine("  Por artefato:");

        var largura = run.Artifacts.Max(a => a.Path.Length);

        foreach (var artifact in run.Artifacts)
        {
            var percent = Percent(artifact.Compliance.Overall.Percent);
            sb.Append("    ")
              .Append(artifact.Path.PadRight(largura))
              .Append("  ")
              .AppendLine(color ? AnsiColors.Bold + percent + AnsiColors.Reset : percent);
        }
    }
```

E acrescentar ao topo do arquivo:

```csharp
using PpLint.Core.Reporting;
```

- [ ] **Step 4: Ajustar o Program**

Em `src/PpLint.Cli/Program.cs`, dentro de `RunCheck`, trocar a acumulação e a chamada do
reporter. Substituir as declarações de `diagnostics`/`tallies` e o laço por:

```csharp
        var stopwatch = Stopwatch.StartNew();
        var resultados = new List<(string Path, LintResult Result)>();
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

            LintResult result;
            try
            {
                result = engine.Run(project, config, SuppressionIndex.Build(project));
            }
            catch (ConfigException ex)
            {
                // Uma regra pode rejeitar a configuração ao rodar — regex inválido
                // em pp-lint.toml, por exemplo. Sem isto o processo morria com
                // stack trace em vez de sair com código 2 e uma mensagem útil.
                stderr.WriteLine(ex.Message);
                return 2;
            }

            resultados.Add((path, result));
        }

        stopwatch.Stop();

        var run = AnalysisRun.From(resultados, stopwatch.Elapsed);
        var useColor = !options.NoColor && !Console.IsOutputRedirected;

        stdout.Write(TextReporter.Render(run, useColor, options.Quiet));
```

E trocar as duas referências posteriores a `diagnostics` por `run.AllDiagnostics`:

```csharp
        if (!usedConfigFile && run.AllDiagnostics.Any(d => d.Category == RuleCategory.Naming))
```

```csharp
        return run.AllDiagnostics.Any(d => d.Severity >= config.FailOn) ? 1 : 0;
```

Acrescentar ao topo do arquivo:

```csharp
using PpLint.Core.Reporting;
```

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA. Se algum teste antigo de `TextReporter` quebrar por causa da assinatura,
adapte a chamada dele para `AnalysisRun.From([...], elapsed)` — o comportamento com um
artefato é o mesmo de antes.

- [ ] **Step 6: Conferir na mão que a saída de um artefato não mudou**

Run: `dotnet run --project src/PpLint.Cli -- check tests/fixtures/chess-real.msapp`
Expected: mesma saída de antes, sem seção "Por artefato".

Run: `dotnet run --project src/PpLint.Cli -- check tests/fixtures/chess-real.msapp tests/fixtures/solucao-exemplo.zip --quiet`
Expected: seção "Por artefato" com dois números diferentes, e o geral abaixo.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: índice de conformidade por artefato no relatório de texto"
```

---

### Task 3: Documentação de regra embarcada

**Files:**
- Create: `src/PpLint.Rules/RuleDocs.cs`
- Create: `src/PpLint.Rules/Docs/NM001.md`, `NM002.md`, `NM003.md`, `NM010.md`, `NM011.md`
- Modify: `src/PpLint.Rules/PpLint.Rules.csproj`
- Test: `tests/PpLint.Rules.Tests/RuleDocsTests.cs`

**Interfaces:**
- Produces:
  - `sealed record RuleDoc(string Id, string Title, string Summary, string Markdown)`
  - `static class RuleDocs` com `RuleDoc? Find(string ruleId)` e `IReadOnlyList<string> AvailableIds()`

O documento é a fonte única: alimenta o `explain`, o campo `help` do SARIF e o arquivo
publicado em `docs/rules/`. O parser extrai o título da primeira linha e o resumo do
primeiro parágrafo da seção "O que pega"; o markdown inteiro fica disponível para quem
quiser imprimir tudo.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/RuleDocsTests.cs`:

```csharp
using PpLint.Rules;

namespace PpLint.Rules.Tests;

public class RuleDocsTests
{
    [Fact]
    public void FindsAnExistingDocument()
    {
        var doc = RuleDocs.Find("NM010");

        Assert.NotNull(doc);
        Assert.Equal("NM010", doc!.Id);
    }

    [Fact]
    public void TitleComesFromTheFirstHeading()
    {
        var doc = RuleDocs.Find("NM010")!;

        Assert.False(string.IsNullOrWhiteSpace(doc.Title));
        Assert.DoesNotContain("#", doc.Title);
        Assert.DoesNotContain("NM010", doc.Title);
    }

    [Fact]
    public void SummaryComesFromWhatItCatches()
    {
        var doc = RuleDocs.Find("NM010")!;

        Assert.False(string.IsNullOrWhiteSpace(doc.Summary));
        Assert.DoesNotContain("##", doc.Summary);
        // O resumo é uma frase, não a seção inteira: vai para shortDescription
        // do SARIF, que os visualizadores mostram numa linha.
        Assert.True(doc.Summary.Length < 300, $"resumo longo demais: {doc.Summary.Length} caracteres");
    }

    [Fact]
    public void MarkdownKeepsTheWholeDocument()
    {
        var doc = RuleDocs.Find("NM010")!;

        Assert.Contains("## O que pega", doc.Markdown);
        Assert.Contains("## Por que importa", doc.Markdown);
        Assert.Contains("## Exemplo", doc.Markdown);
    }

    [Fact]
    public void UnknownRuleReturnsNull() => Assert.Null(RuleDocs.Find("XX999"));

    [Fact]
    public void LookupIsCaseInsensitive() => Assert.NotNull(RuleDocs.Find("nm010"));

    [Fact]
    public void AvailableIdsAreSorted()
    {
        var ids = RuleDocs.AvailableIds();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Fact]
    public void EveryAvailableDocumentParses()
    {
        // Um documento malformado não pode explodir só quando alguém pedir
        // 'explain' daquela regra específica.
        foreach (var id in RuleDocs.AvailableIds())
        {
            var doc = RuleDocs.Find(id);

            Assert.NotNull(doc);
            Assert.False(string.IsNullOrWhiteSpace(doc!.Title), $"{id} sem título");
            Assert.False(string.IsNullOrWhiteSpace(doc.Summary), $"{id} sem resumo");
        }
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter RuleDocsTests`
Expected: FALHA de compilação — `RuleDocs` não existe.

- [ ] **Step 3: Declarar os documentos como recurso embarcado**

Em `src/PpLint.Rules/PpLint.Rules.csproj`, dentro do `ItemGroup` existente:

```xml
    <EmbeddedResource Include="Docs\*.md" />
```

O nome do recurso vira `PpLint.Rules.Docs.NM010.md`.

- [ ] **Step 4: Implementar o leitor**

`src/PpLint.Rules/RuleDocs.cs`:

```csharp
using System.Collections.Concurrent;
using System.Reflection;

namespace PpLint.Rules;

/// <summary>A documentação de uma regra, como o usuário a lê.</summary>
public sealed record RuleDoc(string Id, string Title, string Summary, string Markdown);

/// <summary>
/// Os documentos de regra embarcados no binário. São a fonte única: alimentam o
/// 'pp-lint explain', o campo help do SARIF e o arquivo publicado no repositório.
///
/// Embarcar em vez de ler do disco é o que faz o binário único funcionar — quem
/// baixa o executável não baixa uma pasta de markdown junto.
/// </summary>
public static class RuleDocs
{
    private const string Prefix = "PpLint.Rules.Docs.";

    private static readonly Assembly Owner = typeof(RuleDocs).Assembly;

    private static readonly ConcurrentDictionary<string, RuleDoc?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Os IDs que têm documento, em ordem.</summary>
    public static IReadOnlyList<string> AvailableIds() =>
        Owner.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)
                        && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..^3])
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    /// <summary>O documento da regra, ou null se ela não tiver um.</summary>
    public static RuleDoc? Find(string ruleId) =>
        Cache.GetOrAdd(ruleId, Load);

    private static RuleDoc? Load(string ruleId)
    {
        // O nome do recurso respeita a caixa do arquivo; a consulta do usuário não.
        var nome = Owner.GetManifestResourceNames()
            .FirstOrDefault(n => string.Equals(n, Prefix + ruleId + ".md", StringComparison.OrdinalIgnoreCase));

        if (nome is null)
            return null;

        using var stream = Owner.GetManifestResourceStream(nome)!;
        using var reader = new StreamReader(stream);
        var markdown = reader.ReadToEnd();

        var id = nome[Prefix.Length..^3];

        return new RuleDoc(id, ParseTitle(markdown, id), ParseSummary(markdown), markdown);
    }

    /// <summary>
    /// O título vem da primeira linha, no formato "# NM010 — Texto". O ID é
    /// removido: quem pediu 'explain NM010' já sabe o ID.
    /// </summary>
    private static string ParseTitle(string markdown, string id)
    {
        var primeira = markdown
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));

        if (primeira is null)
            return id;

        var texto = primeira[2..].Trim();

        if (texto.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            texto = texto[id.Length..].TrimStart(' ', '—', '-', ':');

        return texto.Trim();
    }

    /// <summary>
    /// O resumo é o primeiro parágrafo de "## O que pega" — uma frase, porque vai
    /// para o shortDescription do SARIF, que os visualizadores mostram numa linha.
    /// </summary>
    private static string ParseSummary(string markdown)
    {
        var linhas = markdown.Replace("\r\n", "\n").Split('\n');
        var dentro = false;
        var paragrafo = new List<string>();

        foreach (var linha in linhas)
        {
            if (linha.StartsWith("## ", StringComparison.Ordinal))
            {
                if (dentro)
                    break;

                dentro = linha.Contains("O que pega", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!dentro)
                continue;

            if (linha.Trim().Length == 0)
            {
                if (paragrafo.Count > 0)
                    break;

                continue;
            }

            paragrafo.Add(linha.Trim());
        }

        return string.Join(" ", paragrafo);
    }
}
```

- [ ] **Step 5: Escrever os cinco documentos de nomenclatura**

Todos seguem o mesmo gabarito: título com ID e nome, seções "O que pega", "Por que importa"
e "Exemplo" com um trecho ruim e um bom. Os exemplos precisam ser Power Fx ou nomes reais,
não pseudocódigo.

`src/PpLint.Rules/Docs/NM001.md`:

```markdown
# NM001 — Variável global fora do padrão de nome

## O que pega

Variável definida com `Set()` cujo nome não segue o padrão do preset ativo — no preset
`camel-prefix`, o prefixo `var` seguido de maiúscula, como `varTotalPedidos`.

## Por que importa

Power Fx não separa variável global de contexto na sintaxe: dentro de uma fórmula, `total`
e `locTotal` parecem a mesma coisa. O prefixo é o que permite saber, lendo uma linha
solta, se aquele valor vive na tela inteira ou só naquela tela — e portanto se mudá-lo
afeta outro lugar.

## Exemplo

Ruim:

```
Set(total, Sum(colPedidos, Valor))
```

Bom:

```
Set(varTotal, Sum(colPedidos, Valor))
```

## Como configurar

O padrão vem do preset. Para outro, em `pp-lint.toml`:

```toml
[pp-lint.naming]
global-variable = "^g[A-Z][A-Za-z0-9]*$"
```
```

`src/PpLint.Rules/Docs/NM002.md`:

```markdown
# NM002 — Variável de contexto fora do padrão de nome

## O que pega

Variável criada com `UpdateContext()` ou recebida via `Navigate()` cujo nome não segue o
padrão do preset ativo — no preset `camel-prefix`, o prefixo `loc`, como `locCarregando`.

## Por que importa

Variável de contexto só existe dentro de uma tela. Quem lê uma fórmula precisa saber
disso na hora: um nome sem prefixo leva alguém a tentar usá-la em outra tela e receber
branco, sem erro de compilação e sem pista.

## Exemplo

Ruim:

```
UpdateContext({carregando: true})
```

Bom:

```
UpdateContext({locCarregando: true})
```

## Como configurar

```toml
[pp-lint.naming]
context-variable = "^ctx[A-Z][A-Za-z0-9]*$"
```
```

`src/PpLint.Rules/Docs/NM003.md`:

```markdown
# NM003 — Coleção fora do padrão de nome

## O que pega

Coleção criada com `Collect()` ou `ClearCollect()` cujo nome não segue o padrão do preset
ativo — no preset `camel-prefix`, o prefixo `col`, como `colPedidos`.

## Por que importa

Coleção é tabela em memória e se parece com fonte de dados na hora de usar: `Filter(x, …)`
funciona igual para as duas. O prefixo avisa que aquilo é local, não delegável, e que o
conteúdo depende de alguém ter chamado `Collect` antes.

## Exemplo

Ruim:

```
ClearCollect(pedidos, Filter(Pedidos, Status = "Aberto"))
```

Bom:

```
ClearCollect(colPedidos, Filter(Pedidos, Status = "Aberto"))
```

## Como configurar

```toml
[pp-lint.naming]
collection = "^tbl[A-Z][A-Za-z0-9]*$"
```
```

`src/PpLint.Rules/Docs/NM010.md`:

```markdown
# NM010 — Controle com o nome que o Studio gerou

## O que pega

Controle que ficou com o nome automático do Power Apps Studio: `Button1`, `Label7`,
`Rectangle11`, `Gallery2`. São nomes com sufixo numérico e sem nenhuma informação sobre o
que o controle faz.

## Por que importa

O nome do controle é referência de código: outras fórmulas escrevem `Label7.Text`. Quando
o nome não diz nada, cada leitura obriga a abrir o Studio e procurar o controle na tela
para descobrir do que se trata. Pior: inserir um controle novo pode renumerar os
existentes, e aí `Label7` passa a ser outro.

Controles gerados pelo próprio Studio dentro de galerias e formulários — `galleryTemplate`,
`DataCard` — não são cobrados, porque ninguém os escreveu.

## Exemplo

Ruim:

```
Button1
Label7
```

Bom:

```
btnConfirmarPedido
lblTotalPedido
```

## Como configurar

Para tratar outros templates como gerados pelo Studio:

```toml
[pp-lint.naming]
generated-control-templates = ["galleryTemplate", "dataCard", "meuTemplate"]
```
```

`src/PpLint.Rules/Docs/NM011.md`:

```markdown
# NM011 — Prefixo do controle não corresponde ao tipo

## O que pega

Controle cujo prefixo contradiz o tipo real: um `toggle` chamado `btnAtivo`, um `label`
chamado `txtNome`. A regra lê o template do controle no artefato, não adivinha pelo nome.

## Por que importa

Um prefixo errado é pior que prefixo nenhum: quem lê `btnAtivo` numa fórmula assume um
botão e espera `OnSelect`, mas aquilo é um toggle com `OnCheck` e `Value`. O nome mente
sobre o contrato do controle.

## Exemplo

Ruim, para um controle do tipo `toggle`:

```
btnAtivo
```

Bom:

```
tglAtivo
```

## Como configurar

O mapa de prefixos vem do preset. Para ajustar um tipo:

```toml
[pp-lint.naming.control-prefixes]
toggle = "tog"
```

Presets disponíveis: `camel-prefix` (padrão) e `pascal-type`, que espera `ButtonSalvar` em
vez de `btnSalvar`.
```

- [ ] **Step 6: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — 8 testes novos. `AvailableIds()` devolve os cinco IDs de nomenclatura.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: documentação de regra embarcada, com as cinco regras de nomenclatura"
```

---

### Task 4: Documentar as oito regras de fluxo

**Files:**
- Create: `src/PpLint.Rules/Docs/FL201.md`, `FL202.md`, `FL203.md`, `FL210.md`, `FL222.md`, `FL230.md`, `FL240.md`, `FL241.md`
- Test: nenhum arquivo novo — `RuleDocsTests.EveryAvailableDocumentParses` já cobre cada documento acrescentado.

**Interfaces:**
- Consumes: o gabarito e o parser da Task 3.

Mesmo gabarito da Task 3: `# <ID> — <nome>`, seções "O que pega", "Por que importa",
"Exemplo". Em regras de fluxo o exemplo é a configuração da ação descrita em palavras ou o
trecho de JSON relevante, porque não existe linguagem de expressão para mostrar.

- [ ] **Step 1: Escrever os oito documentos**

`src/PpLint.Rules/Docs/FL201.md`:

```markdown
# FL201 — Variável de fluxo inicializada e nunca lida

## O que pega

Ação `Inicializar variável` cujo nome não aparece em nenhuma expressão do fluxo — nem em
`variables('nome')`, nem em `Definir variável`, nem em `Acrescentar a`.

## Por que importa

Cada ação conta no limite de ações do fluxo e no tempo de execução. Uma variável que
ninguém lê costuma ser resto de uma versão anterior: alguém trocou a lógica e esqueceu de
apagar a inicialização.

## Exemplo

Ruim: o fluxo inicializa `contador` e nenhuma ação posterior usa `variables('contador')`.

Bom: remova a ação `Inicializar variável`, ou passe a usar a variável onde ela deveria
entrar.
```

`src/PpLint.Rules/Docs/FL202.md`:

```markdown
# FL202 — Variável usada antes de ser inicializada

## O que pega

Expressão que lê `variables('nome')` numa ação que roda antes — pela cadeia de
`Executar após` — da ação que inicializa aquela variável.

A regra só acusa quando a ordem é conhecida. Ações em ramos paralelos não têm ordem entre
si, e nesse caso ela fica em silêncio: acusar sem certeza num erro seria pior que deixar
passar.

## Por que importa

Ler variável não inicializada não falha o fluxo: devolve nulo. O resultado é um e-mail com
campo vazio, um registro gravado sem valor, um `if` que sempre cai no ramo errado — e nada
disso aparece no histórico de execuções como erro.

## Exemplo

Ruim: `Compor` roda logo após o gatilho e usa `@{variables('total')}`, enquanto
`Inicializar total` roda depois, encadeada em `Compor`.

Bom: inverta o `Executar após`, deixando a inicialização antes de qualquer leitura.
```

`src/PpLint.Rules/Docs/FL203.md`:

```markdown
# FL203 — Saída de ação computacional nunca consumida

## O que pega

Ação sem efeito colateral — `Compor`, `Selecionar`, `Filtrar matriz`, `Analisar JSON`,
`Criar tabela` — cujo nome não aparece em nenhum `outputs('…')`, `body('…')` ou referência
`@Nome` do fluxo.

Ações de conector ficam de fora: enviar e-mail ou criar registro existe pelo efeito, não
pela saída, e cobrar uso do retorno delas seria errado.

## Por que importa

Uma ação de cálculo que ninguém lê é trabalho puro: consome uma ação do limite do fluxo e
tempo de execução para produzir um valor que se perde. Quase sempre é resto de depuração —
um `Compor` inserido para inspecionar um valor e nunca removido.

## Exemplo

Ruim: o fluxo tem `Compor` com a saída `@{items('Aplicar_a_cada')?['id']}` e nenhuma ação
posterior referencia `outputs('Compor')`.

Bom: remova o `Compor`, ou consuma o resultado onde ele deveria entrar.
```

`src/PpLint.Rules/Docs/FL210.md`:

```markdown
# FL210 — Fluxo sem nenhum tratamento de falha

## O que pega

Fluxo com duas ou mais ações em que todo `Executar após` exige apenas `Êxito`. Nenhuma
ação está configurada para rodar quando outra falha, expira ou é ignorada.

## Por que importa

Quando uma ação falha e ninguém trata, o fluxo simplesmente para. Não há e-mail, não há
registro, não há alerta: o problema aparece dias depois como dado faltando, e a única
forma de descobrir é abrir o histórico de execuções e procurar.

Basta uma ação de aviso configurada para rodar em caso de falha para o fluxo deixar de ser
silencioso.

## Exemplo

Ruim: dez ações encadeadas, todas com `Executar após: Êxito`.

Bom: uma ação final `Enviar e-mail` com `Executar após` marcado para `Falhou`,
`Ignorado` e `Atingiu o tempo limite` da ação crítica — ou o trecho crítico dentro de um
`Escopo` com um segundo escopo de tratamento depois dele.
```

`src/PpLint.Rules/Docs/FL222.md`:

```markdown
# FL222 — `Aplicar a cada` dentro de outro

## O que pega

Ação `Aplicar a cada` que está dentro de outra `Aplicar a cada`, em qualquer nível.

## Por que importa

O custo é multiplicativo: cem itens externos e cem internos são dez mil execuções da ação
de dentro. Com uma chamada de conector ali, é o limite de requisições da licença estourando
e o fluxo levando horas.

Quase sempre dá para trocar o laço externo por `Filtrar matriz` ou `Selecionar`, que rodam
como uma ação só.

## Exemplo

Ruim: `Aplicar a cada pedido` contendo `Aplicar a cada item do pedido` com uma chamada de
conector dentro.

Bom: monte a lista final com `Selecionar` ou `Filtrar matriz` antes e percorra uma vez só;
ou ative a concorrência do laço quando a ordem não importar.
```

`src/PpLint.Rules/Docs/FL230.md`:

```markdown
# FL230 — Recorrência mais frequente que o limiar

## O que pega

Gatilho de recorrência cujo intervalo é menor que o limiar configurado — 15 minutos por
padrão.

## Por que importa

Recorrência muito curta quase sempre é polling: o fluxo acorda, pergunta se algo mudou e
na maioria das vezes não fez nada. Cada acordada consome uma execução da licença. A cada 5
minutos são 288 execuções por dia, quase todas inúteis.

Na maior parte dos casos existe gatilho de evento para o mesmo cenário — item criado,
e-mail recebido, registro alterado — que dispara só quando há o que fazer.

## Exemplo

Ruim: recorrência de 5 em 5 minutos verificando se há item novo na lista.

Bom: gatilho `Quando um item é criado`, que dispara no evento.

## Como configurar

```toml
[pp-lint.thresholds]
min-recurrence-minutes = 5
```
```

`src/PpLint.Rules/Docs/FL240.md`:

```markdown
# FL240 — Fluxo sem descrição

## O que pega

Fluxo sem o campo de descrição preenchido.

## Por que importa

Na lista do Power Automate aparece só o nome. Num ambiente com dezenas de fluxos, decidir
se um fluxo pode ser desligado, ou quem chamar quando ele falha, depende de abrir cada um e
ler as ações. Uma linha de descrição resolve isso na lista.

## Exemplo

Ruim: fluxo `AIPromptTimeManagement` sem descrição.

Bom: descrição "Todo dia às 7h, monta o plano do dia a partir da agenda e das tarefas, e
envia por e-mail."
```

`src/PpLint.Rules/Docs/FL241.md`:

```markdown
# FL241 — `Executar após` aponta para ação inexistente

## O que pega

Ação cujo `Executar após` cita o nome de uma ação que não existe no fluxo.

## Por que importa

Quase sempre é resultado de edição manual do JSON exportado, ou de uma ação renomeada ou
apagada fora do designer. O fluxo pode até importar, mas a dependência declarada não existe
— e o comportamento na execução deixa de ser o que o arquivo aparenta.

## Exemplo

Ruim: `Enviar e-mail` declara `Executar após: Obter_itens`, e o fluxo não tem nenhuma ação
com esse nome.

Bom: aponte o `Executar após` para uma ação existente, ou remova a dependência.
```

- [ ] **Step 2: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA. `EveryAvailableDocumentParses` agora percorre 13 documentos e todos
precisam ter título e resumo — se um deles não tiver a seção "O que pega" escrita
exatamente assim, o teste aponta qual.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "docs: documentação das oito regras de Power Automate"
```

---

### Task 5: Documentar as quinze regras de Power Fx

**Files:**
- Create: `src/PpLint.Rules/Docs/PF101.md` … `PF106.md`, `PF110.md` … `PF118.md`
- Test: nenhum arquivo novo — `RuleDocsTests.EveryAvailableDocumentParses` cobre.

**Interfaces:**
- Consumes: o gabarito e o parser da Task 3.

- [ ] **Step 1: Escrever os quinze documentos**

Todos no gabarito `# <ID> — <nome>` com "O que pega", "Por que importa" e "Exemplo". Os
exemplos são Power Fx real, na sintaxe invariante — vírgula separando argumentos e ponto
decimal, como o `.msapp` guarda.

Conteúdo de cada um, na ordem:

- **PF101 — Variável global nunca lida.** Pega `Set(varX, …)` cujo nome não aparece em
  nenhuma outra fórmula do app. Importa porque variável global vive enquanto a sessão
  existe e aparece na lista de variáveis do Studio, empurrando as que interessam para
  baixo; e porque quem lê o `Set` assume que alguém consome aquilo. Exemplo ruim:
  `Set(varTemp, 1)` e nenhuma leitura. Exemplo bom: remover o `Set`.
- **PF102 — Variável de contexto nunca lida.** Mesmo caso para `UpdateContext({locX: …})`
  e para contexto passado em `Navigate`. Importa porque contexto morto engana quem lê o
  `Navigate` e acha que a tela de destino depende daquele valor.
- **PF103 — Coleção nunca lida.** `ClearCollect(colX, …)` cuja coleção nunca é consultada.
  Importa porque `ClearCollect` percorre a fonte inteira e guarda tudo em memória: é o
  desperdício mais caro da lista.
- **PF104 — Nome não resolvido.** Identificador que não é controle, variável, coleção,
  fonte de dados, função ou palavra reservada. Importa porque em Power Fx um nome
  desconhecido não quebra o app: vira branco. Severidade de erro. Exemplo ruim:
  `Set(varTotal, ValorTotall)` com o nome digitado errado.
- **PF105 — Global que poderia ser de contexto.** Variável global lida em uma tela só.
  Importa porque global é estado do app inteiro; restringir à tela deixa explícito o
  alcance e evita que outra tela dependa dela por acidente. Severidade informativa.
  Exemplo: trocar `Set(varFiltro, …)` por `UpdateContext({locFiltro: …})`.
- **PF106 — `Set` redundante.** Duas atribuições seguidas à mesma variável no mesmo bloco,
  sem leitura entre elas e sem desvio entre elas. Importa porque a primeira não tem efeito
  nenhum, e ver duas atribuições sugere que uma delas deveria ser outra coisa. Exemplo
  ruim: `Set(varX, 1); Set(varX, 2)`.
- **PF110 — Condição constante.** `If` cuja condição é literal ou comparação entre
  literais: `If(true, …)`, `If(2 > 1, …)`. Importa porque um dos ramos nunca executa —
  costuma ser depuração esquecida. Exemplo bom: remover o `If` e deixar o ramo que vale.
- **PF111 — `If` que devolve booleano.** `If(cond, true, false)` é `cond`, e
  `If(cond, false, true)` é `Not(cond)`. Importa porque a forma longa esconde a intenção
  atrás de um desvio que não existe.
- **PF112 — Comparação com booleano.** `x = true` é `x`; `x = false` é `Not(x)`. Importa
  pelo mesmo motivo, e porque a comparação sugere que `x` talvez não seja booleano, o que
  confunde quem lê.
- **PF113 — Ramos idênticos.** Todos os ramos do `If` fazem a mesma coisa, então a
  condição não muda o resultado. Severidade de erro: ou a condição é inútil, ou um dos
  ramos está errado. Funções voláteis como `Rand()` e `Now()` ficam de fora, porque duas
  chamadas iguais não produzem o mesmo valor. Exemplo ruim:
  `If(varCond, varA + 1, varA + 1)`.
- **PF114 — Ramo inalcançável.** A mesma condição testada de novo depois de já ter falhado,
  seja na forma aninhada `If(c, a, If(c, b, d))` ou encadeada `If(c, a, c, b, d)`. Se ela
  era falsa para chegar ali, continua falsa: aquele ramo nunca executa. Condições voláteis
  ficam de fora.
- **PF115 — Dupla negação.** `Not(Not(x))` é `x`. Aparece quando alguém inverte uma
  condição duas vezes durante uma refatoração e não simplifica no fim. Numa cadeia mais
  longa, a regra fala uma vez só, no nó mais externo.
- **PF116 — `Filter` sem efeito.** `Filter(fonte, true)` devolve a fonte inteira. Importa
  porque engana quem lê e, sobre fonte delegável, ainda atrapalha a delegação. `false` não
  é acusado: devolve vazio, que é outro problema.
- **PF117 — `CountRows(x) > 0`.** Conta todos os itens só para saber se existe algum;
  `!IsEmpty(x)` para no primeiro. Sobre fonte grande a diferença aparece na tela do
  usuário. As formas reconhecidas incluem `CountRows(x) >= 1` e as duas com os lados
  trocados.
- **PF118 — Concatenação com texto vazio.** `"abc" & ""` não muda nada. Severidade
  informativa. A regra só acusa quando o outro lado já é comprovadamente texto — literal,
  interpolação ou função de texto — porque `varQtd & ""` é o idioma corrente de converter
  número em texto, e removê-lo mudaria o tipo do resultado.

Cada documento traz, na seção "Exemplo", um bloco "Ruim" e um "Bom" em Power Fx, conforme
descrito acima.

- [ ] **Step 2: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA. `AvailableIds()` devolve 28 IDs.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "docs: documentação das quinze regras de Power Fx"
```

---

### Task 6: Contrato de cobertura e comando `explain`

**Files:**
- Modify: `src/PpLint.Cli/Program.cs`
- Test: `tests/PpLint.Rules.Tests/RuleCatalogContractTests.cs` (acrescentar um teste)
- Test: `tests/PpLint.Cli.Tests/ExplainCommandTests.cs`

**Interfaces:**
- Consumes: `RuleDocs.Find(string)`, `RuleDocs.AvailableIds()` da Task 3.

O contrato de cobertura entra agora, e não na Task 3, porque só faz sentido quando os 28
documentos existem — um contrato que nasce vermelho obriga a commitar com a suíte quebrada.

- [ ] **Step 1: Escrever o contrato de cobertura**

Acrescentar a `tests/PpLint.Rules.Tests/RuleCatalogContractTests.cs`:

```csharp
    [Fact]
    public void EveryRuleInTheCatalogHasDocumentation()
    {
        // Regra sem documento faz 'pp-lint explain' e o SARIF saírem mancos
        // justamente para a regra nova, que é a que ninguém conhece.
        var semDoc = RulesAssembly.GetTypes()
            .Select(t => t.GetCustomAttributes(typeof(RuleAttribute), false).FirstOrDefault())
            .OfType<RuleAttribute>()
            .Select(a => a.Id)
            .Where(id => RuleDocs.Find(id) is null)
            .Order()
            .ToList();

        Assert.Empty(semDoc);
    }

    [Fact]
    public void EveryDocumentCorrespondsToARuleInTheCatalog()
    {
        // O contrário também: documento órfão significa regra removida ou ID
        // digitado errado no nome do arquivo.
        var ids = RulesAssembly.GetTypes()
            .Select(t => t.GetCustomAttributes(typeof(RuleAttribute), false).FirstOrDefault())
            .OfType<RuleAttribute>()
            .Select(a => a.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orfaos = RuleDocs.AvailableIds().Where(id => !ids.Contains(id)).ToList();

        Assert.Empty(orfaos);
    }
```

Acrescentar ao topo do arquivo, se ainda não houver:

```csharp
using PpLint.Rules;
```

- [ ] **Step 2: Escrever o teste do comando**

`tests/PpLint.Cli.Tests/ExplainCommandTests.cs`:

```csharp
namespace PpLint.Cli.Tests;

public class ExplainCommandTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr, Directory.GetCurrentDirectory());

        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void ExplainPrintsTheDocument()
    {
        var (code, saida, _) = Run("explain", "NM010");

        Assert.Equal(0, code);
        Assert.Contains("O que pega", saida);
        Assert.Contains("Por que importa", saida);
    }

    [Fact]
    public void ExplainAcceptsLowercaseId()
    {
        var (code, saida, _) = Run("explain", "nm010");

        Assert.Equal(0, code);
        Assert.Contains("O que pega", saida);
    }

    [Fact]
    public void UnknownRuleFailsWithAHelpfulMessage()
    {
        var (code, _, erro) = Run("explain", "XX999");

        Assert.Equal(2, code);
        Assert.Contains("XX999", erro);
        // A mensagem precisa dizer como descobrir os IDs válidos, senão o usuário
        // fica só com "não existe".
        Assert.Contains("pp-lint rules", erro);
    }
}
```

- [ ] **Step 3: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Cli.Tests --filter ExplainCommandTests`
Expected: FALHA — `explain` ainda imprime "disponível a partir da Fase 2" e devolve 0.

- [ ] **Step 4: Implementar o comando**

Em `src/PpLint.Cli/Program.cs`, substituir o caso `CliCommand.Explain` por:

```csharp
            case CliCommand.Explain:
            {
                var doc = RuleDocs.Find(options.ExplainRuleId!);
                if (doc is null)
                {
                    stderr.WriteLine(
                        $"Regra desconhecida: '{options.ExplainRuleId}'. "
                        + "Use 'pp-lint rules' para ver o catálogo.");
                    return 2;
                }

                stdout.WriteLine(doc.Markdown);
                return 0;
            }
```

Acrescentar ao topo do arquivo:

```csharp
using PpLint.Rules;
```

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — 5 testes novos.

- [ ] **Step 6: Conferir na mão**

Run: `dotnet run --project src/PpLint.Cli -- explain PF113`
Expected: o documento da PF113 impresso, com acentuação correta.

Run: `dotnet run --project src/PpLint.Cli -- explain PF999`
Expected: mensagem de regra desconhecida citando `pp-lint rules`, código de saída 2.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: comando explain e contrato de documentação por regra"
```

---

### Task 7: Formato JSON

**Files:**
- Create: `src/PpLint.Cli/JsonReporter.cs`
- Modify: `src/PpLint.Cli/Program.cs`
- Test: `tests/PpLint.Cli.Tests/JsonReporterTests.cs`

**Interfaces:**
- Consumes: `AnalysisRun`, `ArtifactAnalysis` da Task 1.
- Produces: `JsonReporter.Render(AnalysisRun run)` devolvendo `string`.

Esquema, versão 1:

```json
{
  "schemaVersion": 1,
  "tool": { "name": "pp-lint", "version": "0.1.0.0" },
  "summary": { "errors": 1, "warnings": 0, "infos": 1, "durationSeconds": 0.163 },
  "compliance": {
    "percent": 82.3,
    "evaluatedTargets": 12,
    "violations": 2,
    "byCategory": { "Flow": { "percent": 82.3, "evaluatedTargets": 12, "violations": 2 } }
  },
  "artifacts": [
    {
      "path": "sol.zip",
      "compliance": { "percent": 82.3, "evaluatedTargets": 12, "violations": 2, "byCategory": {} },
      "diagnostics": [
        {
          "ruleId": "FL210",
          "category": "Flow",
          "severity": "error",
          "message": "O fluxo … não trata falha em nenhuma ação.",
          "location": { "artifact": "sol.zip", "entry": "Workflows/f.json", "symbol": "F", "line": 0, "column": 0 }
        }
      ]
    }
  ]
}
```

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Cli.Tests/JsonReporterTests.cs`:

```csharp
using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;

namespace PpLint.Cli.Tests;

public class JsonReporterTests
{
    private static SourceLocation Loc(string artifact) => new(artifact, "Controls/1.json", "btnA", 0, 0);

    private static LintResult Result(string artifact) =>
        new(
            [new Diagnostic("NM010", RuleCategory.Naming, Severity.Warning, "nome padrão", Loc(artifact))],
            [new RuleTally("NM010", RuleCategory.Naming, Severity.Warning, 10, 1)]);

    private static JsonElement Render(params (string Path, LintResult Result)[] results) =>
        JsonDocument.Parse(
            JsonReporter.Render(AnalysisRun.From(results, TimeSpan.FromMilliseconds(250)))).RootElement;

    [Fact]
    public void OutputIsValidJsonWithASchemaVersion()
    {
        var root = Render(("a.msapp", Result("a.msapp")));

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void CarriesToolNameAndVersion()
    {
        var tool = Render(("a.msapp", Result("a.msapp"))).GetProperty("tool");

        Assert.Equal("pp-lint", tool.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("version").GetString()));
    }

    [Fact]
    public void SummaryCountsBySeverity()
    {
        var summary = Render(("a.msapp", Result("a.msapp"))).GetProperty("summary");

        Assert.Equal(0, summary.GetProperty("errors").GetInt32());
        Assert.Equal(1, summary.GetProperty("warnings").GetInt32());
        Assert.Equal(0, summary.GetProperty("infos").GetInt32());
        Assert.Equal(0.25, summary.GetProperty("durationSeconds").GetDouble(), 2);
    }

    [Fact]
    public void EachArtifactCarriesItsOwnComplianceAndDiagnostics()
    {
        var artifacts = Render(
            ("a.msapp", Result("a.msapp")),
            ("b.msapp", Result("b.msapp"))).GetProperty("artifacts");

        Assert.Equal(2, artifacts.GetArrayLength());
        Assert.Equal("a.msapp", artifacts[0].GetProperty("path").GetString());
        Assert.Equal(90.0, artifacts[0].GetProperty("compliance").GetProperty("percent").GetDouble(), 1);
        Assert.Equal(1, artifacts[0].GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public void DiagnosticCarriesEveryFieldTheTerminalShows()
    {
        var d = Render(("a.msapp", Result("a.msapp")))
            .GetProperty("artifacts")[0].GetProperty("diagnostics")[0];

        Assert.Equal("NM010", d.GetProperty("ruleId").GetString());
        Assert.Equal("Naming", d.GetProperty("category").GetString());
        Assert.Equal("warning", d.GetProperty("severity").GetString());
        Assert.Equal("nome padrão", d.GetProperty("message").GetString());

        var loc = d.GetProperty("location");
        Assert.Equal("a.msapp", loc.GetProperty("artifact").GetString());
        Assert.Equal("Controls/1.json", loc.GetProperty("entry").GetString());
        Assert.Equal("btnA", loc.GetProperty("symbol").GetString());
    }

    [Fact]
    public void AccentsSurviveAsRealCharactersNotEscapes()
    {
        // O padrão do System.Text.Json escapa não-ASCII como \u00XX, o que torna
        // o arquivo ilegível para quem abre no editor. Em português isso atinge
        // quase toda mensagem.
        var json = JsonReporter.Render(AnalysisRun.From([("a.msapp", Result("a.msapp"))], TimeSpan.Zero));

        Assert.Contains("padrão", json);
        Assert.DoesNotContain("\\u00", json);
    }

    [Fact]
    public void EmptyRunStillProducesValidJson()
    {
        var root = JsonDocument.Parse(
            JsonReporter.Render(AnalysisRun.From([], TimeSpan.Zero))).RootElement;

        Assert.Equal(0, root.GetProperty("artifacts").GetArrayLength());
        Assert.Equal(100.0, root.GetProperty("compliance").GetProperty("percent").GetDouble(), 1);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Cli.Tests --filter JsonReporterTests`
Expected: FALHA de compilação — `JsonReporter` não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.Cli/JsonReporter.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;

namespace PpLint.Cli;

/// <summary>
/// Saída em JSON, para quem automatiza em cima do linter.
///
/// Escrito campo a campo com Utf8JsonWriter em vez de serializado por reflexão:
/// este formato é contrato com quem consome, e renomear uma propriedade C# não
/// pode mudá-lo em silêncio.
/// </summary>
public static class JsonReporter
{
    /// <summary>
    /// Sobe quando o formato mudar de um jeito que quebre quem já consome. Sem
    /// isto, a primeira mudança passaria despercebida do outro lado.
    /// </summary>
    private const int SchemaVersion = 1;

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        // O escape padrão transformaria "padrão" em "padrão". Em português
        // isso atinge quase toda mensagem e torna o arquivo ilegível.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string Render(AnalysisRun run)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Options))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", SchemaVersion);

            w.WriteStartObject("tool");
            w.WriteString("name", "pp-lint");
            w.WriteString("version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
            w.WriteEndObject();

            WriteSummary(w, run);

            w.WritePropertyName("compliance");
            WriteCompliance(w, run.Compliance);

            w.WriteStartArray("artifacts");
            foreach (var artifact in run.Artifacts)
                WriteArtifact(w, artifact);
            w.WriteEndArray();

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSummary(Utf8JsonWriter w, AnalysisRun run)
    {
        w.WriteStartObject("summary");
        w.WriteNumber("errors", run.AllDiagnostics.Count(d => d.Severity == Severity.Error));
        w.WriteNumber("warnings", run.AllDiagnostics.Count(d => d.Severity == Severity.Warning));
        w.WriteNumber("infos", run.AllDiagnostics.Count(d => d.Severity == Severity.Info));
        w.WriteNumber("durationSeconds", Math.Round(run.Elapsed.TotalSeconds, 3));
        w.WriteEndObject();
    }

    private static void WriteArtifact(Utf8JsonWriter w, ArtifactAnalysis artifact)
    {
        w.WriteStartObject();
        w.WriteString("path", artifact.Path);

        w.WritePropertyName("compliance");
        WriteCompliance(w, artifact.Compliance);

        w.WriteStartArray("diagnostics");
        foreach (var d in artifact.Diagnostics)
            WriteDiagnostic(w, d);
        w.WriteEndArray();

        w.WriteEndObject();
    }

    private static void WriteDiagnostic(Utf8JsonWriter w, Diagnostic d)
    {
        w.WriteStartObject();
        w.WriteString("ruleId", d.RuleId);
        w.WriteString("category", d.Category.ToString());
        w.WriteString("severity", SeverityText(d.Severity));
        w.WriteString("message", d.Message);

        w.WriteStartObject("location");
        w.WriteString("artifact", d.Location.ArtifactPath);
        w.WriteString("entry", d.Location.EntryPath);
        if (d.Location.Symbol is null)
            w.WriteNull("symbol");
        else
            w.WriteString("symbol", d.Location.Symbol);
        w.WriteNumber("line", d.Location.Line);
        w.WriteNumber("column", d.Location.Column);
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static void WriteCompliance(Utf8JsonWriter w, ComplianceReport report)
    {
        w.WriteStartObject();
        WriteScoreFields(w, report.Overall);

        w.WriteStartObject("byCategory");
        foreach (var (category, score) in report.ByCategory.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
        {
            w.WriteStartObject(category.ToString());
            WriteScoreFields(w, score);
            w.WriteEndObject();
        }
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static void WriteScoreFields(Utf8JsonWriter w, ComplianceScore score)
    {
        w.WriteNumber("percent", Math.Round(score.Percent, 1));
        w.WriteNumber("evaluatedTargets", score.EvaluatedTargets);
        w.WriteNumber("violations", score.Violations);
    }

    private static string SeverityText(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "info",
    };
}
```

- [ ] **Step 4: Ligar o formato ao Program**

Em `src/PpLint.Cli/Program.cs`, dentro de `RunCheck`, remover o bloco que rejeita formatos
diferentes de `text` e, no lugar da chamada única ao `TextReporter`, escrever:

```csharp
        var saida = options.Format switch
        {
            "json" => JsonReporter.Render(run),
            _ => TextReporter.Render(run, useColor, options.Quiet),
        };

        if (options.OutputPath is not null)
            File.WriteAllText(options.OutputPath, saida);
        else
            stdout.Write(saida);
```

Se `CliOptions` ainda não tiver `OutputPath`, confira o nome real da propriedade em
`src/PpLint.Cli/ArgumentParser.cs` e use aquele — `--output` já é aceito pelo parser.

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — 7 testes novos.

- [ ] **Step 6: Conferir contra o artefato real**

Run: `dotnet run --project src/PpLint.Cli -- check tests/fixtures/solucao-exemplo.zip --format json`
Expected: JSON válido, com `FL210` e `FL240`, acentuação legível, `compliance.percent` igual
ao que o relatório de texto mostra.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: formato JSON com esquema versionado"
```

---

### Task 8: Formato SARIF

**Files:**
- Create: `src/PpLint.Cli/SarifReporter.cs`
- Modify: `src/PpLint.Cli/Program.cs`
- Test: `tests/PpLint.Cli.Tests/SarifReporterTests.cs`

**Interfaces:**
- Consumes: `AnalysisRun` da Task 1, `RuleDocs.Find(string)` da Task 3.
- Produces: `SarifReporter.Render(AnalysisRun run)` devolvendo `string`.

SARIF 2.1.0. Duas decisões que o formato impõe:

**Sem `region` quando não há linha.** SARIF exige `startLine >= 1`. Nossa `Line` é 0 porque
o `.msapp` guarda fórmulas dentro de JSON gerado e o extractor não registra offset.
Inventar linha 1 colocaria a anotação no lugar errado, o que é pior que não ter anotação de
linha. Levamos entrada e símbolo em `logicalLocations`, que existe exatamente para alvos
que não são posição de texto.

**Nível.** `Error` vira `error`, `Warning` vira `warning`, `Info` vira `note` — os três
níveis que o SARIF define.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Cli.Tests/SarifReporterTests.cs`:

```csharp
using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;

namespace PpLint.Cli.Tests;

public class SarifReporterTests
{
    private static SourceLocation Loc(string artifact, int line = 0) =>
        new(artifact, "Controls/1.json", "btnA", line, 0);

    private static LintResult Result(string artifact, Severity severity = Severity.Warning, int line = 0) =>
        new(
            [new Diagnostic("NM010", RuleCategory.Naming, severity, "nome padrão", Loc(artifact, line))],
            [new RuleTally("NM010", RuleCategory.Naming, severity, 10, 1)]);

    private static JsonElement Render(params (string Path, LintResult Result)[] results) =>
        JsonDocument.Parse(
            SarifReporter.Render(AnalysisRun.From(results, TimeSpan.Zero))).RootElement;

    private static JsonElement FirstRun(JsonElement root) => root.GetProperty("runs")[0];

    [Fact]
    public void DeclaresSarifVersionAndSchema()
    {
        var root = Render(("a.msapp", Result("a.msapp")));

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.Contains("sarif-2.1.0", root.GetProperty("$schema").GetString());
    }

    [Fact]
    public void DriverCarriesToolIdentity()
    {
        var driver = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("tool").GetProperty("driver");

        Assert.Equal("pp-lint", driver.GetProperty("name").GetString());
        Assert.Contains("github.com", driver.GetProperty("informationUri").GetString());
    }

    [Fact]
    public void ReportedRulesAppearInTheDriverWithDocumentation()
    {
        var rules = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("tool").GetProperty("driver").GetProperty("rules");

        var rule = rules[0];
        Assert.Equal("NM010", rule.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            rule.GetProperty("shortDescription").GetProperty("text").GetString()));
        Assert.Contains("NM010", rule.GetProperty("helpUri").GetString());
        Assert.Equal("warning", rule.GetProperty("defaultConfiguration").GetProperty("level").GetString());
    }

    [Fact]
    public void OnlyReportedRulesAreDeclared()
    {
        // Declarar as 28 regras num relatório que achou uma polui a aba Security
        // com regras que não têm achado.
        var rules = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("tool").GetProperty("driver").GetProperty("rules");

        Assert.Equal(1, rules.GetArrayLength());
    }

    [Fact]
    public void ResultCarriesLevelAndMessage()
    {
        var result = FirstRun(Render(("a.msapp", Result("a.msapp")))).GetProperty("results")[0];

        Assert.Equal("NM010", result.GetProperty("ruleId").GetString());
        Assert.Equal("warning", result.GetProperty("level").GetString());
        Assert.Equal("nome padrão", result.GetProperty("message").GetProperty("text").GetString());
    }

    [Fact]
    public void InfoBecomesNote()
    {
        var result = FirstRun(Render(("a.msapp", Result("a.msapp", Severity.Info))))
            .GetProperty("results")[0];

        Assert.Equal("note", result.GetProperty("level").GetString());
    }

    [Fact]
    public void LocationPointsAtTheArtifactFile()
    {
        var location = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("results")[0].GetProperty("locations")[0];

        Assert.Equal(
            "a.msapp",
            location.GetProperty("physicalLocation").GetProperty("artifactLocation")
                .GetProperty("uri").GetString());
    }

    [Fact]
    public void NoRegionWhenThereIsNoLine()
    {
        // SARIF exige startLine >= 1. Nossa linha é 0 porque o .msapp guarda
        // fórmula dentro de JSON gerado: inventar linha 1 apontaria a anotação
        // para o lugar errado.
        var physical = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation");

        Assert.False(physical.TryGetProperty("region", out _));
    }

    [Fact]
    public void RegionAppearsWhenThereIsALine()
    {
        var physical = FirstRun(Render(("a.msapp", Result("a.msapp", line: 42))))
            .GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation");

        Assert.Equal(42, physical.GetProperty("region").GetProperty("startLine").GetInt32());
    }

    [Fact]
    public void EntryAndSymbolTravelAsLogicalLocations()
    {
        // A entrada dentro do pacote e o nome do controle não são posição de
        // texto; logicalLocations existe exatamente para isso.
        var logical = FirstRun(Render(("a.msapp", Result("a.msapp"))))
            .GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("logicalLocations");

        var nomes = logical.EnumerateArray()
            .Select(l => l.GetProperty("fullyQualifiedName").GetString())
            .ToList();

        Assert.Contains(nomes, n => n!.Contains("Controls/1.json"));
        Assert.Contains(nomes, n => n!.Contains("btnA"));
    }

    [Fact]
    public void PathSeparatorsAreNormalizedToForwardSlash()
    {
        // URI de SARIF usa barra normal, independente do sistema onde rodou.
        var uri = FirstRun(Render((@"pasta\sub\a.msapp", Result(@"pasta\sub\a.msapp"))))
            .GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation").GetProperty("artifactLocation")
            .GetProperty("uri").GetString();

        Assert.DoesNotContain('\\', uri);
    }

    [Fact]
    public void EmptyRunProducesAValidEmptySarif()
    {
        var root = JsonDocument.Parse(
            SarifReporter.Render(AnalysisRun.From([], TimeSpan.Zero))).RootElement;

        Assert.Equal(0, FirstRun(root).GetProperty("results").GetArrayLength());
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Cli.Tests --filter SarifReporterTests`
Expected: FALHA de compilação — `SarifReporter` não existe.

- [ ] **Step 3: Implementar**

`src/PpLint.Cli/SarifReporter.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;
using PpLint.Rules;

namespace PpLint.Cli;

/// <summary>
/// Saída em SARIF 2.1.0, o formato que o GitHub lê nativamente: subido com a
/// action upload-sarif, cada achado vira anotação no diff do pull request e
/// entrada na aba Security.
/// </summary>
public static class SarifReporter
{
    private const string Repository = "https://github.com/alvesmaia/pp-lint";

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string Render(AnalysisRun run)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Options))
        {
            w.WriteStartObject();
            w.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
            w.WriteString("version", "2.1.0");

            w.WriteStartArray("runs");
            w.WriteStartObject();

            WriteTool(w, run);
            WriteResults(w, run);

            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteTool(Utf8JsonWriter w, AnalysisRun run)
    {
        w.WriteStartObject("tool");
        w.WriteStartObject("driver");
        w.WriteString("name", "pp-lint");
        w.WriteString("informationUri", Repository);
        w.WriteString("version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");

        w.WriteStartArray("rules");

        // Só as regras que produziram achado. Declarar as 28 num relatório que
        // achou uma polui a aba Security com regras sem resultado.
        var reportadas = run.AllDiagnostics
            .GroupBy(d => d.RuleId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var grupo in reportadas)
            WriteRule(w, grupo.Key, grupo.First());

        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteRule(Utf8JsonWriter w, string ruleId, Diagnostic exemplo)
    {
        var doc = RuleDocs.Find(ruleId);

        w.WriteStartObject();
        w.WriteString("id", ruleId);
        w.WriteString("name", doc?.Title ?? ruleId);

        w.WriteStartObject("shortDescription");
        w.WriteString("text", doc?.Summary ?? doc?.Title ?? ruleId);
        w.WriteEndObject();

        if (doc is not null)
        {
            w.WriteStartObject("fullDescription");
            w.WriteString("text", doc.Summary);
            w.WriteEndObject();

            w.WriteStartObject("help");
            w.WriteString("markdown", doc.Markdown);
            w.WriteString("text", doc.Summary);
            w.WriteEndObject();
        }

        w.WriteString("helpUri", $"{Repository}/blob/main/docs/rules/{ruleId}.md");

        w.WriteStartObject("defaultConfiguration");
        w.WriteString("level", Level(exemplo.Severity));
        w.WriteEndObject();

        w.WriteStartObject("properties");
        w.WriteString("category", exemplo.Category.ToString());
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static void WriteResults(Utf8JsonWriter w, AnalysisRun run)
    {
        w.WriteStartArray("results");

        foreach (var d in run.AllDiagnostics)
        {
            w.WriteStartObject();
            w.WriteString("ruleId", d.RuleId);
            w.WriteString("level", Level(d.Severity));

            w.WriteStartObject("message");
            w.WriteString("text", d.Message);
            w.WriteEndObject();

            w.WriteStartArray("locations");
            WriteLocation(w, d.Location);
            w.WriteEndArray();

            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteLocation(Utf8JsonWriter w, SourceLocation location)
    {
        w.WriteStartObject();

        w.WriteStartObject("physicalLocation");
        w.WriteStartObject("artifactLocation");
        w.WriteString("uri", Uri(location.ArtifactPath));
        w.WriteEndObject();

        // SARIF exige startLine >= 1. Quando não temos linha, omitir a região é
        // mais honesto que apontar para a linha 1.
        if (location.Line > 0)
        {
            w.WriteStartObject("region");
            w.WriteNumber("startLine", location.Line);
            if (location.Column > 0)
                w.WriteNumber("startColumn", location.Column);
            w.WriteEndObject();
        }

        w.WriteEndObject();

        var logicos = new List<string>();
        if (!string.IsNullOrEmpty(location.EntryPath))
            logicos.Add(location.EntryPath);
        if (!string.IsNullOrEmpty(location.Symbol))
            logicos.Add(location.Symbol!);

        if (logicos.Count > 0)
        {
            w.WriteStartArray("logicalLocations");
            foreach (var nome in logicos)
            {
                w.WriteStartObject();
                w.WriteString("fullyQualifiedName", nome);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        w.WriteEndObject();
    }

    /// <summary>URI de SARIF usa barra normal, venha de onde vier o caminho.</summary>
    private static string Uri(string path) => path.Replace('\\', '/');

    private static string Level(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "note",
    };
}
```

- [ ] **Step 4: Ligar o formato ao Program**

Em `src/PpLint.Cli/Program.cs`, acrescentar o caso ao switch de formato:

```csharp
        var saida = options.Format switch
        {
            "json" => JsonReporter.Render(run),
            "sarif" => SarifReporter.Render(run),
            _ => TextReporter.Render(run, useColor, options.Quiet),
        };
```

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — 12 testes novos.

- [ ] **Step 6: Conferir contra o artefato real**

Run: `dotnet run --project src/PpLint.Cli -- check tests/fixtures/chess-real.msapp --format sarif --output /tmp/pp.sarif`
Expected: arquivo escrito. Conferir que `runs[0].tool.driver.rules` tem uma entrada por
regra com achado, que cada uma traz `helpUri`, e que nenhum `physicalLocation` tem `region`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: formato SARIF 2.1.0 para anotação inline em pull request"
```

---

### Task 9: Parar de prometer HTML e Markdown, e publicar as docs

**Files:**
- Modify: `src/PpLint.Cli/ArgumentParser.cs`
- Modify: `src/PpLint.Cli/Program.cs` (texto de ajuda)
- Create: `docs/rules/` (cópia publicada dos documentos embarcados)
- Modify: `README.md`
- Modify: `PROGRESSO.md`
- Test: `tests/PpLint.Cli.Tests/ArgumentParserTests.cs` (acrescentar)
- Test: `tests/PpLint.Rules.Tests/RuleDocsTests.cs` (acrescentar)

**Interfaces:**
- Consumes: `RuleDocs.AvailableIds()` da Task 3.

Os `helpUri` do SARIF apontam para `docs/rules/<ID>.md` no repositório. Se a pasta não
existir, todo link do relatório dá 404 — e o link quebrado aparece justamente para quem
clicou querendo entender o achado.

- [ ] **Step 1: Escrever os testes que falham**

Acrescentar a `tests/PpLint.Cli.Tests/ArgumentParserTests.cs`:

```csharp
public class RemovedFormatsTests
{
    [Fact]
    public void HtmlIsRejectedWithTheListOfWhatWorks()
    {
        var ex = Assert.Throws<ConfigException>(() =>
            ArgumentParser.Parse(["check", "a.msapp", "--format", "html"]));

        Assert.Contains("html", ex.Message);
        Assert.Contains("text", ex.Message);
        Assert.Contains("json", ex.Message);
        Assert.Contains("sarif", ex.Message);
    }

    [Fact]
    public void MarkdownIsRejected() =>
        Assert.Throws<ConfigException>(() =>
            ArgumentParser.Parse(["check", "a.msapp", "--format", "md"]));

    [Fact]
    public void TheThreeSupportedFormatsAreAccepted()
    {
        foreach (var formato in new[] { "text", "json", "sarif" })
            Assert.Equal(formato, ArgumentParser.Parse(["check", "a.msapp", "--format", formato]).Format);
    }
}
```

Se o parser lançar um tipo de exceção diferente de `ConfigException`, use o tipo real —
confira a linha que hoje monta a mensagem "Formato inválido".

Acrescentar a `tests/PpLint.Rules.Tests/RuleDocsTests.cs`:

```csharp
    [Fact]
    public void EveryEmbeddedDocumentIsAlsoPublishedInTheRepository()
    {
        // O helpUri do SARIF aponta para docs/rules/<ID>.md no GitHub. Documento
        // que existe só embarcado vira link quebrado para quem clicou querendo
        // entender o achado.
        var raiz = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var pasta = Path.Combine(raiz, "docs", "rules");

        Assert.True(Directory.Exists(pasta), $"pasta não encontrada: {pasta}");

        var ausentes = RuleDocs.AvailableIds()
            .Where(id => !File.Exists(Path.Combine(pasta, id + ".md")))
            .ToList();

        Assert.Empty(ausentes);
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test`
Expected: FALHA — `html` ainda é aceito e `docs/rules/` não existe.

- [ ] **Step 3: Reduzir os formatos válidos**

Em `src/PpLint.Cli/ArgumentParser.cs`, trocar a lista:

```csharp
    private static readonly string[] ValidFormats = ["text", "json", "sarif"];
```

- [ ] **Step 4: Corrigir o texto de ajuda**

Em `src/PpLint.Cli/Program.cs`, dentro de `HelpText`, trocar a linha do formato:

```
          --format <text|json|sarif>            formato de saída (padrão: text)
```

- [ ] **Step 5: Publicar os documentos**

Os arquivos embarcados e os publicados precisam ser o mesmo conteúdo. Copiar:

```bash
mkdir -p docs/rules
cp src/PpLint.Rules/Docs/*.md docs/rules/
```

E criar `docs/rules/README.md`:

```markdown
# Documentação das regras

Um arquivo por regra do catálogo. São os mesmos documentos embarcados no binário — o que
`pp-lint explain <ID>` imprime, e o que os `helpUri` dos relatórios SARIF referenciam.

Ao alterar uma regra, altere `src/PpLint.Rules/Docs/<ID>.md` e copie para cá. O teste
`EveryEmbeddedDocumentIsAlsoPublishedInTheRepository` falha se um documento existir só de
um lado.
```

- [ ] **Step 6: Atualizar o README**

Na seção de uso, após a tabela de regras, acrescentar:

```markdown
## Formatos de saída

| Formato | Para quê |
|---|---|
| `text` | leitura no terminal, com índice de conformidade (padrão) |
| `json` | automação; esquema versionado em `schemaVersion` |
| `sarif` | anotação inline em pull request e aba Security do GitHub |

Exemplo de uso em GitHub Actions:

```yaml
- run: pp-lint check solucao.zip --format sarif --output pp-lint.sarif
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: pp-lint.sarif
```

`pp-lint explain <ID>` imprime a documentação completa de uma regra; os mesmos textos estão
em [`docs/rules/`](docs/rules/).
```

- [ ] **Step 7: Atualizar o PROGRESSO**

Acrescentar ao fim de `PROGRESSO.md`:

```markdown
## Fase 2c — concluída

Formatos `json` e `sarif`, comando `explain` com documentação das 28 regras embarcada no
binário, e índice de conformidade por artefato. `html` e `md` saíram do `--help`: voltam
na Fase 5, junto com o relatório HTML que o spec prevê.

O SARIF omite `region` quando não há número de linha, em vez de apontar para a linha 1.
O `.msapp` guarda as fórmulas dentro de JSON gerado pelo Studio e o extractor não registra
offset; a anotação aponta para o arquivo, com entrada e símbolo em `logicalLocations`.

Plano: `docs/superpowers/plans/2026-08-19-pp-lint-fase-2c.md`.
```

- [ ] **Step 8: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA.

- [ ] **Step 9: Validação final contra os dois artefatos reais**

```bash
dotnet run --project src/PpLint.Cli -- check tests/fixtures/chess-real.msapp tests/fixtures/solucao-exemplo.zip --quiet
dotnet run --project src/PpLint.Cli -- check tests/fixtures/chess-real.msapp --format json | head -30
dotnet run --project src/PpLint.Cli -- check tests/fixtures/solucao-exemplo.zip --format sarif | head -40
dotnet run --project src/PpLint.Cli -- explain FL210
dotnet run --project src/PpLint.Cli -- check tests/fixtures/chess-real.msapp --format html
```

Expected, na ordem: seção "Por artefato" com dois índices distintos; JSON válido com
acentuação legível; SARIF com `helpUri` por regra; documento da FL210 impresso; e o último
comando recusado com a lista `text, json, sarif`.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: formatos reduzidos ao que o CLI cumpre, e docs de regra publicadas"
```

---

## Auto-revisão

**Cobertura da fase.** Os quatro itens combinados com o usuário estão cobertos: JSON
(Task 7), SARIF (Task 8), `explain` com documentação completa das 28 regras (Tasks 3 a 6) e
índice por artefato (Tasks 1 e 2). A remoção de HTML e Markdown está na Task 9.

**Consistência de tipos.** `AnalysisRun.From` recebe `IEnumerable<(string Path, LintResult
Result)>` e é chamada assim nas Tasks 2, 7 e 8. `TextReporter.Render(AnalysisRun, bool,
bool)` é definida na Task 2 e usada nas Tasks 7 e 8. `RuleDocs.Find` devolve `RuleDoc?` e é
consumida nas Tasks 6 e 8 com verificação de nulo. `RuleDoc.Summary` alimenta
`shortDescription` e `fullDescription` do SARIF, e o teste da Task 3 limita seu tamanho a
300 caracteres justamente por isso.

**Ordem das tasks.** O contrato de cobertura de documentação está na Task 6, depois de os
28 documentos existirem (Tasks 3 a 5) — um contrato criado antes nasceria vermelho e
obrigaria a commitar com a suíte quebrada, contra a regra global.

**Risco conhecido.** A Task 2 muda a assinatura pública de `TextReporter.Render`. Testes
existentes que a chamam vão parar de compilar; o Step 5 daquela task manda adaptá-los, e o
comportamento com um artefato é idêntico ao de hoje.
