# pp-lint Fase 1 — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar um binário `pp-lint` que analisa uma solução Power Platform exportada (`.zip`), um app (`.msapp`) ou uma pasta, aplica 5 regras piloto e imprime um relatório com índice de conformidade.

**Architecture:** Pipeline `ArchiveReader → Extractors → PowerPlatformProject (IR) → RuleEngine → Diagnostics → ComplianceScorer → TextReporter`. As regras enxergam apenas o IR, nunca o formato de origem. Tudo é somente-leitura: nenhum artefato é modificado.

**Tech Stack:** .NET 10, C# 14, xUnit, `Microsoft.PowerFx.Core`. Parser de argumentos e renderização ANSI são próprios — sem `System.CommandLine`, sem `Spectre.Console`.

**Spec:** `docs/superpowers/specs/2026-08-18-pp-lint-design.md`

## Global Constraints

- **.NET 10** (`net10.0`), `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` em todos os projetos.
- **Somente-leitura absoluto.** Nenhum código pode abrir arquivo para escrita fora de `--output`. Zip sempre aberto com `ZipArchiveMode.Read`.
- **Dependência de runtime única:** `Microsoft.PowerFx.Core`. Qualquer pacote adicional exige justificativa explícita e compatibilidade com AOT.
- **Mensagens de diagnóstico em português do Brasil**, com acentuação correta. Identificadores de código em inglês.
- **IDs de regra são imutáveis.** Um ID nunca é reciclado nem renumerado.
- **Invariante do índice:** toda regra emite no máximo uma violação por alvo avaliado (`V(r) ≤ E(r)`).
- **Pesos de severidade:** `Error = 10`, `Warning = 3`, `Info = 1`.
- **Exit codes:** `0` sem achados acima de `fail-on`; `1` achados acima de `fail-on`; `2` erro de execução.
- **Toda regra chama `ctx.Evaluated(n)`** para cada alvo examinado, mesmo quando não reporta nada.
- **TDD obrigatório:** teste que falha antes de qualquer implementação. Commit ao fim de cada task.

## Estrutura de arquivos da Fase 1

| Arquivo | Responsabilidade |
|---|---|
| `src/PpLint.Core/Severity.cs` | Enum de severidade e pesos |
| `src/PpLint.Core/RuleCategory.cs` | Enum de categoria |
| `src/PpLint.Core/SourceLocation.cs` | Localização de um achado dentro do artefato |
| `src/PpLint.Core/Diagnostic.cs` | Registro de um achado |
| `src/PpLint.Core/Model/*.cs` | IR: projeto, app, controle, fluxo, tabela, data source |
| `src/PpLint.Core/Rules/IRule.cs` | Contrato de regra e atributo `[Rule]` |
| `src/PpLint.Core/Rules/LintContext.cs` | Contexto passado às regras (`Report`, `Evaluated`) |
| `src/PpLint.Core/Rules/RuleEngine.cs` | Descoberta e execução de regras |
| `src/PpLint.Core/Scoring/ComplianceScorer.cs` | Cálculo do índice de conformidade |
| `src/PpLint.Core/PpLintConfig.cs` | Configuração com defaults embutidos |
| `src/PpLint.Extractors/IArtifactSource.cs` | Abstração de leitura (zip ou pasta) |
| `src/PpLint.Extractors/ZipArtifactSource.cs` | Leitura de `.zip`/`.msapp` |
| `src/PpLint.Extractors/DirectoryArtifactSource.cs` | Leitura de pasta descompactada |
| `src/PpLint.Extractors/MsappExtractor.cs` | Controles, telas, data sources de um `.msapp` |
| `src/PpLint.Extractors/SolutionExtractor.cs` | `solution.xml`, apps aninhados, workflows |
| `src/PpLint.Extractors/FlowExtractor.cs` | Trigger, ações e variáveis de um cloud flow |
| `src/PpLint.Extractors/ProjectLoader.cs` | Ponto de entrada: caminho → `PowerPlatformProject` |
| `src/PpLint.PowerFx/PowerFxParser.cs` | Wrapper de parse do Power Fx |
| `src/PpLint.PowerFx/AstWalker.cs` | Percurso de AST e coleta de nós |
| `src/PpLint.PowerFx/VariableGraph.cs` | Definições e leituras de variáveis |
| `src/PpLint.Rules/Naming/DefaultControlNameRule.cs` | NM010 |
| `src/PpLint.Rules/Naming/ControlPrefixRule.cs` | NM011 |
| `src/PpLint.Rules/Fx/UnusedGlobalVariableRule.cs` | PF101 |
| `src/PpLint.Rules/Fx/ConstantConditionRule.cs` | PF110 |
| `src/PpLint.Rules/Flow/UnusedFlowVariableRule.cs` | FL201 |
| `src/PpLint.Cli/ArgumentParser.cs` | Parser de linha de comando próprio |
| `src/PpLint.Cli/TextReporter.cs` | Relatório de terminal com índice |
| `src/PpLint.Cli/Program.cs` | Wiring e exit codes |

---

### Task 1: Scaffolding da solução e tipos de diagnóstico

**Files:**
- Create: `pp-lint.sln`, `Directory.Build.props`, `.gitignore`
- Create: `src/PpLint.Core/PpLint.Core.csproj`, `src/PpLint.Core/Severity.cs`, `src/PpLint.Core/RuleCategory.cs`, `src/PpLint.Core/SourceLocation.cs`, `src/PpLint.Core/Diagnostic.cs`
- Test: `tests/PpLint.Core.Tests/PpLint.Core.Tests.csproj`, `tests/PpLint.Core.Tests/DiagnosticTests.cs`

**Interfaces:**
- Consumes: nada (primeira task).
- Produces: `Severity` (`Info`/`Warning`/`Error`) com `SeverityWeights.Of(Severity) → int`; `RuleCategory`; `SourceLocation(string ArtifactPath, string EntryPath, string? Symbol, int Line, int Column)` com `ToString()`; `Diagnostic(string RuleId, RuleCategory Category, Severity Severity, string Message, SourceLocation Location)`.

- [ ] **Step 1: Criar a estrutura da solução**

```bash
cd /c/PROJETOS/pp-lint
git init
dotnet new sln -n pp-lint
dotnet new classlib -o src/PpLint.Core -f net10.0
dotnet new xunit -o tests/PpLint.Core.Tests -f net10.0
rm src/PpLint.Core/Class1.cs tests/PpLint.Core.Tests/UnitTest1.cs
dotnet sln add src/PpLint.Core/PpLint.Core.csproj tests/PpLint.Core.Tests/PpLint.Core.Tests.csproj
dotnet add tests/PpLint.Core.Tests reference src/PpLint.Core
```

- [ ] **Step 2: Criar `Directory.Build.props` na raiz**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

> Não ative `InvariantGlobalization`: o relatório formata percentuais em pt-BR (vírgula decimal) e, sob globalização invariante, `CultureInfo.GetCultureInfo("pt-BR")` devolve a cultura invariante — os números sairiam com ponto sem que nada falhasse no build.

- [ ] **Step 3: Criar `.gitignore`**

```gitignore
bin/
obj/
*.user
artifacts/
```

- [ ] **Step 4: Escrever o teste que falha**

`tests/PpLint.Core.Tests/DiagnosticTests.cs`:

```csharp
using PpLint.Core;

namespace PpLint.Core.Tests;

public class DiagnosticTests
{
    [Theory]
    [InlineData(Severity.Error, 10)]
    [InlineData(Severity.Warning, 3)]
    [InlineData(Severity.Info, 1)]
    public void SeverityWeights_MatchSpec(Severity severity, int expected)
        => Assert.Equal(expected, SeverityWeights.Of(severity));

    [Fact]
    public void SourceLocation_FormatsHumanReadablePath()
    {
        var loc = new SourceLocation("MinhaSolucao.zip", "CanvasApps/App.msapp", "btnSalvar.OnSelect", 12, 8);
        Assert.Equal("MinhaSolucao.zip > CanvasApps/App.msapp > btnSalvar.OnSelect:12:8", loc.ToString());
    }

    [Fact]
    public void SourceLocation_OmitsSymbolAndPositionWhenAbsent()
    {
        var loc = new SourceLocation("App.msapp", "Controls/1.json", null, 0, 0);
        Assert.Equal("App.msapp > Controls/1.json", loc.ToString());
    }

    [Fact]
    public void Diagnostic_CarriesRuleMetadata()
    {
        var loc = new SourceLocation("a.zip", "b.json", null, 0, 0);
        var d = new Diagnostic("NM010", RuleCategory.Naming, Severity.Error, "mensagem", loc);
        Assert.Equal("NM010", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }
}
```

- [ ] **Step 5: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests`
Expected: FALHA de compilação — `Severity`, `SeverityWeights`, `SourceLocation` e `Diagnostic` não existem.

- [ ] **Step 6: Implementar os tipos**

`src/PpLint.Core/Severity.cs`:

```csharp
namespace PpLint.Core;

public enum Severity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public static class SeverityWeights
{
    public static int Of(Severity severity) => severity switch
    {
        Severity.Error => 10,
        Severity.Warning => 3,
        Severity.Info => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };
}
```

`src/PpLint.Core/RuleCategory.cs`:

```csharp
namespace PpLint.Core;

public enum RuleCategory
{
    Naming,
    PowerFx,
    Flow,
    Duplication,
    Performance,
    Security,
    Solution,
}
```

`src/PpLint.Core/SourceLocation.cs`:

```csharp
using System.Text;

namespace PpLint.Core;

/// <summary>
/// Onde um achado vive dentro do artefato analisado.
/// <paramref name="ArtifactPath"/> é o caminho do arquivo passado na linha de comando;
/// <paramref name="EntryPath"/> é a entrada dentro do pacote; <paramref name="Symbol"/>
/// é o nome legível (controle, propriedade, ação de fluxo).
/// </summary>
public sealed record SourceLocation(
    string ArtifactPath,
    string EntryPath,
    string? Symbol,
    int Line,
    int Column)
{
    public override string ToString()
    {
        var sb = new StringBuilder(ArtifactPath);
        if (!string.IsNullOrEmpty(EntryPath))
            sb.Append(" > ").Append(EntryPath);
        if (!string.IsNullOrEmpty(Symbol))
            sb.Append(" > ").Append(Symbol);
        if (Line > 0)
            sb.Append(':').Append(Line).Append(':').Append(Column);
        return sb.ToString();
    }
}
```

`src/PpLint.Core/Diagnostic.cs`:

```csharp
namespace PpLint.Core;

public sealed record Diagnostic(
    string RuleId,
    RuleCategory Category,
    Severity Severity,
    string Message,
    SourceLocation Location);
```

- [ ] **Step 7: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Core.Tests`
Expected: PASSA — 5 testes.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: scaffolding da solução e tipos de diagnóstico"
```

---

### Task 2: Parser de argumentos

**Files:**
- Create: `src/PpLint.Cli/PpLint.Cli.csproj`, `src/PpLint.Cli/ArgumentParser.cs`
- Test: `tests/PpLint.Cli.Tests/PpLint.Cli.Tests.csproj`, `tests/PpLint.Cli.Tests/ArgumentParserTests.cs`

**Interfaces:**
- Consumes: `Severity` da Task 1.
- Produces: `enum CliCommand { Check, Explain, Rules, Version, Help }`; `sealed record CliOptions(CliCommand Command, IReadOnlyList<string> Paths, string Format, string? Output, Severity FailOn, bool NoColor, bool Quiet, string? ExplainRuleId)`; `static ParseResult<CliOptions> ArgumentParser.Parse(string[] args)` onde `ParseResult<T>` expõe `bool IsSuccess`, `T? Value`, `string? Error`.

- [ ] **Step 1: Criar o projeto**

```bash
cd /c/PROJETOS/pp-lint
dotnet new console -o src/PpLint.Cli -f net10.0
dotnet new xunit -o tests/PpLint.Cli.Tests -f net10.0
rm tests/PpLint.Cli.Tests/UnitTest1.cs
dotnet sln add src/PpLint.Cli/PpLint.Cli.csproj tests/PpLint.Cli.Tests/PpLint.Cli.Tests.csproj
dotnet add src/PpLint.Cli reference src/PpLint.Core
dotnet add tests/PpLint.Cli.Tests reference src/PpLint.Cli
```

Editar `src/PpLint.Cli/PpLint.Cli.csproj` e acrescentar dentro do `<PropertyGroup>`:

```xml
    <AssemblyName>pp-lint</AssemblyName>
    <RootNamespace>PpLint.Cli</RootNamespace>
```

- [ ] **Step 2: Escrever o teste que falha**

`tests/PpLint.Cli.Tests/ArgumentParserTests.cs`:

```csharp
using PpLint.Core;

namespace PpLint.Cli.Tests;

public class ArgumentParserTests
{
    [Fact]
    public void Parse_CheckWithSinglePath()
    {
        var r = ArgumentParser.Parse(["check", "MinhaSolucao.zip"]);
        Assert.True(r.IsSuccess);
        Assert.Equal(CliCommand.Check, r.Value!.Command);
        Assert.Equal(["MinhaSolucao.zip"], r.Value.Paths);
        Assert.Equal("text", r.Value.Format);
        Assert.Equal(Severity.Error, r.Value.FailOn);
    }

    [Fact]
    public void Parse_CheckWithMultiplePathsAndOptions()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "b.msapp", "--format", "json", "--fail-on", "warning", "--no-color"]);
        Assert.True(r.IsSuccess);
        Assert.Equal(["a.zip", "b.msapp"], r.Value!.Paths);
        Assert.Equal("json", r.Value.Format);
        Assert.Equal(Severity.Warning, r.Value.FailOn);
        Assert.True(r.Value.NoColor);
    }

    [Fact]
    public void Parse_ExplainCapturesRuleId()
    {
        var r = ArgumentParser.Parse(["explain", "PF101"]);
        Assert.True(r.IsSuccess);
        Assert.Equal(CliCommand.Explain, r.Value!.Command);
        Assert.Equal("PF101", r.Value.ExplainRuleId);
    }

    [Fact]
    public void Parse_NoArgsIsHelp()
    {
        var r = ArgumentParser.Parse([]);
        Assert.True(r.IsSuccess);
        Assert.Equal(CliCommand.Help, r.Value!.Command);
    }

    [Fact]
    public void Parse_UnknownCommandFails()
    {
        var r = ArgumentParser.Parse(["frobnicate"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("frobnicate", r.Error);
    }

    [Fact]
    public void Parse_CheckWithoutPathFails()
    {
        var r = ArgumentParser.Parse(["check"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("caminho", r.Error);
    }

    [Fact]
    public void Parse_OptionMissingValueFails()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--format"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("--format", r.Error);
    }

    [Fact]
    public void Parse_InvalidFormatFails()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--format", "pdf"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("pdf", r.Error);
    }

    [Fact]
    public void Parse_InvalidFailOnFails()
    {
        var r = ArgumentParser.Parse(["check", "a.zip", "--fail-on", "critical"]);
        Assert.False(r.IsSuccess);
        Assert.Contains("critical", r.Error);
    }
}
```

- [ ] **Step 3: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Cli.Tests`
Expected: FALHA de compilação — `ArgumentParser` não existe.

- [ ] **Step 4: Implementar o parser**

`src/PpLint.Cli/ArgumentParser.cs`:

```csharp
using PpLint.Core;

namespace PpLint.Cli;

public enum CliCommand { Check, Explain, Rules, Version, Help }

public sealed record CliOptions(
    CliCommand Command,
    IReadOnlyList<string> Paths,
    string Format,
    string? Output,
    Severity FailOn,
    bool NoColor,
    bool Quiet,
    string? ExplainRuleId);

public sealed record ParseResult<T>(bool IsSuccess, T? Value, string? Error)
{
    public static ParseResult<T> Ok(T value) => new(true, value, null);
    public static ParseResult<T> Fail(string error) => new(false, default, error);
}

public static class ArgumentParser
{
    private static readonly string[] ValidFormats = ["text", "json", "sarif", "html", "md"];

    public static ParseResult<CliOptions> Parse(string[] args)
    {
        if (args.Length == 0)
            return Ok(CliCommand.Help);

        var command = args[0] switch
        {
            "check" => CliCommand.Check,
            "explain" => CliCommand.Explain,
            "rules" => CliCommand.Rules,
            "--version" or "-v" or "version" => CliCommand.Version,
            "--help" or "-h" or "help" => CliCommand.Help,
            _ => (CliCommand?)null,
        };

        if (command is null)
            return ParseResult<CliOptions>.Fail($"Comando desconhecido: '{args[0]}'. Use 'pp-lint --help'.");

        var paths = new List<string>();
        var format = "text";
        string? output = null;
        var failOn = Severity.Error;
        var noColor = false;
        var quiet = false;
        string? explainRuleId = null;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--format":
                case "--output":
                case "--fail-on":
                    if (i + 1 >= args.Length)
                        return ParseResult<CliOptions>.Fail($"A opção {arg} exige um valor.");
                    var value = args[++i];
                    if (arg == "--format")
                    {
                        if (!ValidFormats.Contains(value))
                            return ParseResult<CliOptions>.Fail(
                                $"Formato inválido: '{value}'. Válidos: {string.Join(", ", ValidFormats)}.");
                        format = value;
                    }
                    else if (arg == "--output")
                    {
                        output = value;
                    }
                    else
                    {
                        var parsed = ParseSeverity(value);
                        if (parsed is null)
                            return ParseResult<CliOptions>.Fail(
                                $"Severidade inválida: '{value}'. Válidas: error, warning, info.");
                        failOn = parsed.Value;
                    }
                    break;

                case "--no-color":
                    noColor = true;
                    break;

                case "--quiet":
                    quiet = true;
                    break;

                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                        return ParseResult<CliOptions>.Fail($"Opção desconhecida: '{arg}'.");
                    if (command == CliCommand.Explain)
                        explainRuleId = arg;
                    else
                        paths.Add(arg);
                    break;
            }
        }

        if (command == CliCommand.Check && paths.Count == 0)
            return ParseResult<CliOptions>.Fail("O comando 'check' exige ao menos um caminho de artefato.");

        if (command == CliCommand.Explain && explainRuleId is null)
            return ParseResult<CliOptions>.Fail("O comando 'explain' exige um ID de regra, por exemplo 'PF101'.");

        return ParseResult<CliOptions>.Ok(new CliOptions(
            command.Value, paths, format, output, failOn, noColor, quiet, explainRuleId));
    }

    private static Severity? ParseSeverity(string value) => value.ToLowerInvariant() switch
    {
        "error" => Severity.Error,
        "warning" => Severity.Warning,
        "info" => Severity.Info,
        _ => null,
    };

    private static ParseResult<CliOptions> Ok(CliCommand command) =>
        ParseResult<CliOptions>.Ok(new CliOptions(command, [], "text", null, Severity.Error, false, false, null));
}
```

- [ ] **Step 5: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Cli.Tests`
Expected: PASSA — 9 testes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: parser de argumentos do CLI"
```

---

### Task 3: Leitura de artefatos (zip e pasta)

**Files:**
- Create: `src/PpLint.Extractors/PpLint.Extractors.csproj`, `src/PpLint.Extractors/IArtifactSource.cs`, `src/PpLint.Extractors/ZipArtifactSource.cs`, `src/PpLint.Extractors/DirectoryArtifactSource.cs`, `src/PpLint.Extractors/ArtifactSourceFactory.cs`
- Test: `tests/PpLint.Extractors.Tests/PpLint.Extractors.Tests.csproj`, `tests/PpLint.Extractors.Tests/ArtifactSourceTests.cs`, `tests/PpLint.Extractors.Tests/TestZip.cs`

**Interfaces:**
- Consumes: nada da Task 2.
- Produces: `interface IArtifactSource : IDisposable` com `string Path { get; }`, `IEnumerable<string> Entries { get; }`, `bool Has(string entry)`, `string ReadText(string entry)`, `Stream OpenRead(string entry)`, `IArtifactSource? OpenNested(string entry)`; `static IArtifactSource ArtifactSourceFactory.Open(string path)` que lança `ArtifactException` para caminho inexistente ou pacote inválido; `sealed class ArtifactException : Exception`. Caminhos de entrada são sempre normalizados com `/` e comparados case-insensitive.

- [ ] **Step 1: Criar o projeto**

```bash
cd /c/PROJETOS/pp-lint
dotnet new classlib -o src/PpLint.Extractors -f net10.0
dotnet new xunit -o tests/PpLint.Extractors.Tests -f net10.0
rm src/PpLint.Extractors/Class1.cs tests/PpLint.Extractors.Tests/UnitTest1.cs
dotnet sln add src/PpLint.Extractors/PpLint.Extractors.csproj tests/PpLint.Extractors.Tests/PpLint.Extractors.Tests.csproj
dotnet add src/PpLint.Extractors reference src/PpLint.Core
dotnet add tests/PpLint.Extractors.Tests reference src/PpLint.Extractors
```

- [ ] **Step 2: Escrever o helper de fixtures**

`tests/PpLint.Extractors.Tests/TestZip.cs`:

```csharp
using System.IO.Compression;
using System.Text;

namespace PpLint.Extractors.Tests;

/// <summary>Cria zips temporários em memória de disco para os testes.</summary>
public static class TestZip
{
    public static string Create(params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var e = zip.CreateEntry(entry);
            using var writer = new StreamWriter(e.Open(), Encoding.UTF8);
            writer.Write(content);
        }
        return path;
    }

    public static string CreateNested(string innerEntry, string innerZipPath, params (string, string)[] outerEntries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (entry, content) in outerEntries)
        {
            var e = zip.CreateEntry(entry);
            using var writer = new StreamWriter(e.Open(), Encoding.UTF8);
            writer.Write(content);
        }
        var nested = zip.CreateEntry(innerEntry);
        using (var target = nested.Open())
        using (var source = File.OpenRead(innerZipPath))
            source.CopyTo(target);
        return path;
    }
}
```

- [ ] **Step 3: Escrever o teste que falha**

`tests/PpLint.Extractors.Tests/ArtifactSourceTests.cs`:

```csharp
namespace PpLint.Extractors.Tests;

public class ArtifactSourceTests
{
    [Fact]
    public void Zip_ListsEntriesAndReadsText()
    {
        var zip = TestZip.Create(("solution.xml", "<x/>"), ("Workflows/a.json", "{}"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.Contains("solution.xml", src.Entries);
        Assert.Contains("Workflows/a.json", src.Entries);
        Assert.Equal("<x/>", src.ReadText("solution.xml"));
    }

    [Fact]
    public void Zip_HasIsCaseInsensitive()
    {
        var zip = TestZip.Create(("Solution.xml", "<x/>"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.True(src.Has("solution.xml"));
        Assert.Equal("<x/>", src.ReadText("SOLUTION.XML"));
    }

    [Fact]
    public void Zip_NormalizesBackslashSeparators()
    {
        var zip = TestZip.Create((@"CanvasApps\App.msapp", "conteudo"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.Contains("CanvasApps/App.msapp", src.Entries);
    }

    [Fact]
    public void Zip_OpensNestedArchive()
    {
        var inner = TestZip.Create(("Controls/1.json", "{\"a\":1}"));
        var outer = TestZip.CreateNested("CanvasApps/App.msapp", inner, ("solution.xml", "<x/>"));
        using var src = ArtifactSourceFactory.Open(outer);
        using var nested = src.OpenNested("CanvasApps/App.msapp");
        Assert.NotNull(nested);
        Assert.Equal("{\"a\":1}", nested!.ReadText("Controls/1.json"));
        Assert.Equal("CanvasApps/App.msapp", nested.Path);
    }

    [Fact]
    public void Directory_ListsFilesRecursively()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pplint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "Src"));
        File.WriteAllText(Path.Combine(dir, "Src", "App.fx.yaml"), "conteudo");
        using var src = ArtifactSourceFactory.Open(dir);
        Assert.Contains("Src/App.fx.yaml", src.Entries);
        Assert.Equal("conteudo", src.ReadText("Src/App.fx.yaml"));
    }

    [Fact]
    public void MissingPath_ThrowsArtifactException()
    {
        var ex = Assert.Throws<ArtifactException>(() => ArtifactSourceFactory.Open("nao-existe.zip"));
        Assert.Contains("nao-existe.zip", ex.Message);
    }

    [Fact]
    public void CorruptZip_ThrowsArtifactException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-{Guid.NewGuid():N}.zip");
        File.WriteAllText(path, "isto nao e um zip");
        var ex = Assert.Throws<ArtifactException>(() => ArtifactSourceFactory.Open(path));
        Assert.Contains("não é um pacote zip válido", ex.Message);
    }

    [Fact]
    public void ReadingUnknownEntry_ThrowsArtifactException()
    {
        var zip = TestZip.Create(("a.txt", "x"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.Throws<ArtifactException>(() => src.ReadText("b.txt"));
    }

    [Fact]
    public void OpenNested_ReturnsNullForNonArchiveEntry()
    {
        var zip = TestZip.Create(("a.txt", "isto nao e um zip"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.Null(src.OpenNested("a.txt"));
    }
}
```

- [ ] **Step 4: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Extractors.Tests`
Expected: FALHA de compilação — `ArtifactSourceFactory` não existe.

- [ ] **Step 5: Implementar a abstração**

`src/PpLint.Extractors/IArtifactSource.cs`:

```csharp
namespace PpLint.Extractors;

/// <summary>
/// Acesso somente-leitura ao conteúdo de um artefato — um pacote zip
/// (.zip / .msapp) ou uma pasta descompactada.
/// Todos os caminhos de entrada usam '/' e são comparados sem diferenciar maiúsculas.
/// </summary>
public interface IArtifactSource : IDisposable
{
    /// <summary>Caminho do artefato, como exibido nos diagnósticos.</summary>
    string Path { get; }

    IEnumerable<string> Entries { get; }

    bool Has(string entry);

    string ReadText(string entry);

    Stream OpenRead(string entry);

    /// <summary>
    /// Abre um pacote aninhado (ex.: um .msapp dentro de uma solução).
    /// Retorna null quando a entrada existe mas não é um pacote válido.
    /// </summary>
    IArtifactSource? OpenNested(string entry);
}

public sealed class ArtifactException : Exception
{
    public ArtifactException(string message) : base(message) { }
    public ArtifactException(string message, Exception inner) : base(message, inner) { }
}

internal static class EntryPath
{
    public static string Normalize(string entry) => entry.Replace('\\', '/').TrimStart('/');
}
```

`src/PpLint.Extractors/ZipArtifactSource.cs`:

```csharp
using System.IO.Compression;
using System.Text;

namespace PpLint.Extractors;

public sealed class ZipArtifactSource : IArtifactSource
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly Stream? _ownedStream;
    private readonly List<IDisposable> _nested = [];

    private ZipArtifactSource(string path, ZipArchive archive, Stream? ownedStream)
    {
        Path = path;
        _archive = archive;
        _ownedStream = ownedStream;
        _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in archive.Entries)
        {
            if (e.FullName.EndsWith('/'))
                continue;
            _entries[EntryPath.Normalize(e.FullName)] = e;
        }
    }

    public string Path { get; }

    public IEnumerable<string> Entries => _entries.Keys;

    public static ZipArtifactSource OpenFile(string path)
    {
        try
        {
            var stream = File.OpenRead(path);
            var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            return new ZipArtifactSource(path, archive, stream);
        }
        catch (InvalidDataException ex)
        {
            throw new ArtifactException($"O arquivo '{path}' não é um pacote zip válido.", ex);
        }
    }

    /// <summary>Abre um pacote a partir de um stream já carregado em memória.</summary>
    public static ZipArtifactSource? TryOpenStream(string logicalPath, Stream seekable)
    {
        try
        {
            var archive = new ZipArchive(seekable, ZipArchiveMode.Read, leaveOpen: false);
            return new ZipArtifactSource(logicalPath, archive, seekable);
        }
        catch (InvalidDataException)
        {
            seekable.Dispose();
            return null;
        }
    }

    public bool Has(string entry) => _entries.ContainsKey(EntryPath.Normalize(entry));

    public Stream OpenRead(string entry)
    {
        var key = EntryPath.Normalize(entry);
        if (!_entries.TryGetValue(key, out var e))
            throw new ArtifactException($"Entrada não encontrada em '{Path}': {entry}");
        return e.Open();
    }

    public string ReadText(string entry)
    {
        using var stream = OpenRead(entry);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public IArtifactSource? OpenNested(string entry)
    {
        // ZipArchiveEntry.Open() não é seekable; copiamos para memória antes de reabrir.
        var buffer = new MemoryStream();
        using (var stream = OpenRead(entry))
            stream.CopyTo(buffer);
        buffer.Position = 0;

        var nested = TryOpenStream(EntryPath.Normalize(entry), buffer);
        if (nested is not null)
            _nested.Add(nested);
        return nested;
    }

    public void Dispose()
    {
        foreach (var n in _nested)
            n.Dispose();
        _archive.Dispose();
        _ownedStream?.Dispose();
    }
}
```

`src/PpLint.Extractors/DirectoryArtifactSource.cs`:

```csharp
using System.Text;

namespace PpLint.Extractors;

public sealed class DirectoryArtifactSource : IArtifactSource
{
    private readonly string _root;
    private readonly Dictionary<string, string> _entries;
    private readonly List<IDisposable> _nested = [];

    public DirectoryArtifactSource(string root)
    {
        _root = System.IO.Path.GetFullPath(root);
        Path = root;
        _entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(_root, file);
            _entries[EntryPath.Normalize(relative)] = file;
        }
    }

    public string Path { get; }

    public IEnumerable<string> Entries => _entries.Keys;

    public bool Has(string entry) => _entries.ContainsKey(EntryPath.Normalize(entry));

    public Stream OpenRead(string entry)
    {
        var key = EntryPath.Normalize(entry);
        if (!_entries.TryGetValue(key, out var full))
            throw new ArtifactException($"Entrada não encontrada em '{Path}': {entry}");
        return File.OpenRead(full);
    }

    public string ReadText(string entry)
    {
        using var stream = OpenRead(entry);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public IArtifactSource? OpenNested(string entry)
    {
        var buffer = new MemoryStream();
        using (var stream = OpenRead(entry))
            stream.CopyTo(buffer);
        buffer.Position = 0;

        var nested = ZipArtifactSource.TryOpenStream(EntryPath.Normalize(entry), buffer);
        if (nested is not null)
            _nested.Add(nested);
        return nested;
    }

    public void Dispose()
    {
        foreach (var n in _nested)
            n.Dispose();
    }
}
```

`src/PpLint.Extractors/ArtifactSourceFactory.cs`:

```csharp
namespace PpLint.Extractors;

public static class ArtifactSourceFactory
{
    public static IArtifactSource Open(string path)
    {
        if (Directory.Exists(path))
            return new DirectoryArtifactSource(path);

        if (!File.Exists(path))
            throw new ArtifactException($"Caminho não encontrado: '{path}'.");

        return ZipArtifactSource.OpenFile(path);
    }
}
```

- [ ] **Step 6: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Extractors.Tests`
Expected: PASSA — 9 testes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: leitura somente-leitura de artefatos zip e pasta"
```

---

### Task 4: Modelo de domínio (IR)

**Files:**
- Create: `src/PpLint.Core/Model/PowerPlatformProject.cs`, `src/PpLint.Core/Model/SolutionInfo.cs`, `src/PpLint.Core/Model/CanvasApp.cs`, `src/PpLint.Core/Model/Control.cs`, `src/PpLint.Core/Model/CloudFlow.cs`, `src/PpLint.Core/Model/DataTable.cs`
- Test: `tests/PpLint.Core.Tests/ModelTests.cs`

**Interfaces:**
- Consumes: `SourceLocation` da Task 1.
- Produces:
  - `PowerPlatformProject` com `string SourcePath`, `SolutionInfo? Solution`, `List<CanvasApp> Apps`, `List<CloudFlow> Flows`, `List<DataTable> Tables`.
  - `SolutionInfo(string UniqueName, string PublisherPrefix, string Version, bool Managed)`.
  - `CanvasApp` com `string Name`, `List<Control> Screens`, `List<DataSource> DataSources`, `SourceLocation Location`, `IEnumerable<Control> AllControls()` (percurso em profundidade, incluindo as telas).
  - `Control` com `string Name`, `string TemplateName`, `Control? Parent`, `List<Control> Children`, `List<PowerFxProperty> Properties`, `SourceLocation Location`, `bool IsScreen`.
  - `PowerFxProperty(string Name, string Script, SourceLocation Location)`.
  - `DataSource(string Name, string Kind, IReadOnlyList<string> Columns)`.
  - `CloudFlow` com `string Name`, `FlowTrigger? Trigger`, `List<FlowAction> Actions`, `List<FlowVariable> Variables`, `SourceLocation Location`, `IEnumerable<FlowAction> AllActions()`.
  - `FlowTrigger(string Name, string Type, SourceLocation Location)`.
  - `FlowAction` com `string Name`, `string Type`, `List<string> RunAfter`, `List<string> Expressions`, `List<FlowAction> Children`, `SourceLocation Location`.
  - `FlowVariable(string Name, string Type, SourceLocation Location)`.
  - `DataTable(string LogicalName, string SchemaName, IReadOnlyList<DataColumn> Columns)` e `DataColumn(string LogicalName, string SchemaName, string Type, bool Required)`.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/ModelTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Core.Tests;

public class ModelTests
{
    private static SourceLocation Loc(string symbol) => new("a.msapp", "Controls/1.json", symbol, 0, 0);

    [Fact]
    public void AllControls_WalksTreeDepthFirstIncludingScreens()
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", Location = Loc("scrHome"), IsScreen = true };
        var container = new Control { Name = "cntTopo", TemplateName = "groupContainer", Location = Loc("cntTopo") };
        var button = new Control { Name = "btnSalvar", TemplateName = "button", Location = Loc("btnSalvar") };

        screen.AddChild(container);
        container.AddChild(button);

        var app = new CanvasApp { Name = "AppVendas", Location = Loc(null!) };
        app.Screens.Add(screen);

        Assert.Equal(["scrHome", "cntTopo", "btnSalvar"], app.AllControls().Select(c => c.Name));
    }

    [Fact]
    public void AddChild_SetsParent()
    {
        var parent = new Control { Name = "scrHome", TemplateName = "screen", Location = Loc("scrHome"), IsScreen = true };
        var child = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        parent.AddChild(child);

        Assert.Same(parent, child.Parent);
        Assert.Contains(child, parent.Children);
    }

    [Fact]
    public void Control_ScreenOf_ReturnsOwningScreen()
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", Location = Loc("scrHome"), IsScreen = true };
        var container = new Control { Name = "cnt", TemplateName = "groupContainer", Location = Loc("cnt") };
        var button = new Control { Name = "btn", TemplateName = "button", Location = Loc("btn") };
        screen.AddChild(container);
        container.AddChild(button);

        Assert.Same(screen, button.ScreenOf());
        Assert.Same(screen, screen.ScreenOf());
    }

    [Fact]
    public void AllActions_WalksNestedActions()
    {
        var loop = new FlowAction { Name = "Apply_to_each", Type = "Foreach", Location = Loc("Apply_to_each") };
        var inner = new FlowAction { Name = "Get_item", Type = "OpenApiConnection", Location = Loc("Get_item") };
        loop.Children.Add(inner);

        var flow = new CloudFlow { Name = "AprovarPedido", Location = Loc(null!) };
        flow.Actions.Add(loop);

        Assert.Equal(["Apply_to_each", "Get_item"], flow.AllActions().Select(a => a.Name));
    }

    [Fact]
    public void Project_StartsEmpty()
    {
        var p = new PowerPlatformProject { SourcePath = "x.zip" };
        Assert.Empty(p.Apps);
        Assert.Empty(p.Flows);
        Assert.Empty(p.Tables);
        Assert.Null(p.Solution);
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests`
Expected: FALHA de compilação — o namespace `PpLint.Core.Model` não existe.

- [ ] **Step 3: Implementar o modelo**

`src/PpLint.Core/Model/SolutionInfo.cs`:

```csharp
namespace PpLint.Core.Model;

public sealed record SolutionInfo(
    string UniqueName,
    string PublisherPrefix,
    string Version,
    bool Managed);
```

`src/PpLint.Core/Model/Control.cs`:

```csharp
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
```

`src/PpLint.Core/Model/CanvasApp.cs`:

```csharp
namespace PpLint.Core.Model;

public sealed class CanvasApp
{
    public required string Name { get; init; }

    public required SourceLocation Location { get; init; }

    public List<Control> Screens { get; } = [];

    public List<DataSource> DataSources { get; } = [];

    /// <summary>Expressões de nível de app, como App.OnStart.</summary>
    public List<PowerFxProperty> AppProperties { get; } = [];

    public IEnumerable<Control> AllControls() =>
        Screens.SelectMany(s => s.SelfAndDescendants());
}
```

`src/PpLint.Core/Model/CloudFlow.cs`:

```csharp
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
```

`src/PpLint.Core/Model/DataTable.cs`:

```csharp
namespace PpLint.Core.Model;

public sealed record DataColumn(
    string LogicalName,
    string SchemaName,
    string Type,
    bool Required);

public sealed record DataTable(
    string LogicalName,
    string SchemaName,
    IReadOnlyList<DataColumn> Columns,
    SourceLocation Location);
```

`src/PpLint.Core/Model/PowerPlatformProject.cs`:

```csharp
namespace PpLint.Core.Model;

/// <summary>
/// Representação intermediária de tudo que foi extraído do artefato.
/// As regras enxergam somente este modelo — nunca o formato de origem.
/// </summary>
public sealed class PowerPlatformProject
{
    public required string SourcePath { get; init; }

    public SolutionInfo? Solution { get; set; }

    public List<CanvasApp> Apps { get; } = [];

    public List<CloudFlow> Flows { get; } = [];

    public List<DataTable> Tables { get; } = [];
}
```

- [ ] **Step 4: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Core.Tests`
Expected: PASSA — 10 testes (5 da Task 1 + 5 desta).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: modelo de domínio (IR) do projeto Power Platform"
```

---

### Task 5: MsappExtractor

**Files:**
- Create: `src/PpLint.Extractors/MsappExtractor.cs`
- Test: `tests/PpLint.Extractors.Tests/MsappExtractorTests.cs`

**Interfaces:**
- Consumes: `IArtifactSource` (Task 3); `CanvasApp`, `Control`, `PowerFxProperty`, `DataSource`, `SourceLocation` (Tasks 1 e 4).
- Produces: `static CanvasApp MsappExtractor.Extract(IArtifactSource source, string artifactPath, string appName)`.

**Formato de entrada.** Um `.msapp` traz um ou mais `Controls/*.json`. Cada arquivo tem um objeto raiz com a propriedade `TopParent`, que é a tela; controles filhos ficam em `Children`, recursivamente. Cada controle tem `Name`, `Template.Name` e `Rules[]`, e cada regra tem `Property` e `InvariantScript` (a fórmula Power Fx). O `DataSources/DataSources.json` tem `DataSources[]`, cada uma com `Name`, `Type` e um schema em `DataEntityMetadataJson` ou em `Columns`.

O extractor é **tolerante**: campo ausente vira valor vazio, arquivo malformado é ignorado, e nunca lança exceção por conteúdo inesperado. Isso é deliberado — um `.msapp` de versão antiga não pode derrubar a análise inteira.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Extractors.Tests/MsappExtractorTests.cs`:

```csharp
using PpLint.Core.Model;

namespace PpLint.Extractors.Tests;

public class MsappExtractorTests
{
    private const string ControlsJson = """
    {
      "TopParent": {
        "Name": "scrPedidos",
        "Template": { "Name": "screen" },
        "Rules": [
          { "Property": "OnVisible", "InvariantScript": "Set(varTotal; 0)" }
        ],
        "Children": [
          {
            "Name": "btnSalvar",
            "Template": { "Name": "button" },
            "Rules": [
              { "Property": "OnSelect", "InvariantScript": "Notify(\"ok\")" },
              { "Property": "Text", "InvariantScript": "\"Salvar\"" }
            ],
            "Children": []
          },
          {
            "Name": "Label1",
            "Template": { "Name": "label" },
            "Children": []
          }
        ]
      }
    }
    """;

    private const string DataSourcesJson = """
    {
      "DataSources": [
        {
          "Name": "Pedidos",
          "Type": "OptionSetInfo",
          "Columns": [ { "Name": "Title" }, { "Name": "Data de Entrega" } ]
        }
      ]
    }
    """;

    [Fact]
    public void Extract_ReadsScreenAndControlTree()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        Assert.Equal("AppVendas", app.Name);
        var screen = Assert.Single(app.Screens);
        Assert.Equal("scrPedidos", screen.Name);
        Assert.True(screen.IsScreen);
        Assert.Equal(["scrPedidos", "btnSalvar", "Label1"], app.AllControls().Select(c => c.Name));
    }

    [Fact]
    public void Extract_ReadsTemplateNames()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        var button = app.AllControls().Single(c => c.Name == "btnSalvar");
        Assert.Equal("button", button.TemplateName);
    }

    [Fact]
    public void Extract_ReadsPowerFxProperties()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        var button = app.AllControls().Single(c => c.Name == "btnSalvar");
        Assert.Equal(2, button.Properties.Count);
        var onSelect = button.Properties.Single(p => p.Name == "OnSelect");
        Assert.Equal("Notify(\"ok\")", onSelect.Script);
        Assert.Equal("btnSalvar.OnSelect", onSelect.Location.Symbol);
        Assert.Equal("Controls/1.json", onSelect.Location.EntryPath);
    }

    [Fact]
    public void Extract_ControlWithoutRulesHasNoProperties()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        Assert.Empty(app.AllControls().Single(c => c.Name == "Label1").Properties);
    }

    [Fact]
    public void Extract_ReadsDataSourcesWithColumns()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson), ("DataSources/DataSources.json", DataSourcesJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        var ds = Assert.Single(app.DataSources);
        Assert.Equal("Pedidos", ds.Name);
        Assert.Equal(["Title", "Data de Entrega"], ds.Columns);
    }

    [Fact]
    public void Extract_ReadsAppOnStartFromProperties()
    {
        const string props = """
        { "LocalConnectionReferences": "", "OnStart": "Set(varUsuario; User().Email)" }
        """;
        var zip = TestZip.Create(("Controls/1.json", ControlsJson), ("Properties.json", props));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        var onStart = Assert.Single(app.AppProperties);
        Assert.Equal("OnStart", onStart.Name);
        Assert.Equal("Set(varUsuario; User().Email)", onStart.Script);
        Assert.Equal("App.OnStart", onStart.Location.Symbol);
    }

    [Fact]
    public void Extract_MultipleControlFilesProduceMultipleScreens()
    {
        const string second = """
        { "TopParent": { "Name": "scrDetalhe", "Template": { "Name": "screen" }, "Children": [] } }
        """;
        var zip = TestZip.Create(("Controls/1.json", ControlsJson), ("Controls/2.json", second));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        Assert.Equal(2, app.Screens.Count);
        Assert.Contains(app.Screens, s => s.Name == "scrDetalhe");
    }

    [Fact]
    public void Extract_MalformedControlFileIsSkipped()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson), ("Controls/2.json", "{ isto nao e json"));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        Assert.Single(app.Screens);
    }

    [Fact]
    public void Extract_EmptyPackageProducesEmptyApp()
    {
        var zip = TestZip.Create(("Header.json", "{}"));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVazio");

        Assert.Empty(app.Screens);
        Assert.Empty(app.DataSources);
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter MsappExtractorTests`
Expected: FALHA de compilação — `MsappExtractor` não existe.

- [ ] **Step 3: Implementar o extractor**

`src/PpLint.Extractors/MsappExtractor.cs`:

```csharp
using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Extractors;

/// <summary>
/// Extrai o modelo de um canvas app a partir do pacote .msapp cru,
/// sem depender do pac CLI. Tolerante a campos ausentes e arquivos
/// malformados: o que não puder ser lido é ignorado silenciosamente.
/// </summary>
public static class MsappExtractor
{
    public static CanvasApp Extract(IArtifactSource source, string artifactPath, string appName)
    {
        var app = new CanvasApp
        {
            Name = appName,
            Location = new SourceLocation(artifactPath, string.Empty, null, 0, 0),
        };

        foreach (var entry in ControlEntries(source))
        {
            var screen = TryReadScreen(source, artifactPath, entry);
            if (screen is not null)
                app.Screens.Add(screen);
        }

        ReadAppProperties(source, artifactPath, app);
        ReadDataSources(source, artifactPath, app);

        return app;
    }

    private static IEnumerable<string> ControlEntries(IArtifactSource source) =>
        source.Entries
            .Where(e => e.StartsWith("Controls/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

    private static Control? TryReadScreen(IArtifactSource source, string artifactPath, string entry)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(source.ReadText(entry));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArtifactException)
        {
            return null;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("TopParent", out var top) || top.ValueKind != JsonValueKind.Object)
                return null;
            return ReadControl(top, artifactPath, entry, isScreen: true);
        }
    }

    private static Control ReadControl(JsonElement element, string artifactPath, string entry, bool isScreen)
    {
        var name = GetString(element, "Name") ?? "(sem nome)";
        var template = element.TryGetProperty("Template", out var t) && t.ValueKind == JsonValueKind.Object
            ? GetString(t, "Name") ?? string.Empty
            : string.Empty;

        var control = new Control
        {
            Name = name,
            TemplateName = template,
            IsScreen = isScreen,
            Location = new SourceLocation(artifactPath, entry, name, 0, 0),
        };

        if (element.TryGetProperty("Rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in rules.EnumerateArray())
            {
                var property = GetString(rule, "Property");
                var script = GetString(rule, "InvariantScript");
                if (property is null || script is null)
                    continue;

                control.Properties.Add(new PowerFxProperty(
                    property,
                    script,
                    new SourceLocation(artifactPath, entry, $"{name}.{property}", 0, 0)));
            }
        }

        if (element.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object)
                    control.AddChild(ReadControl(child, artifactPath, entry, isScreen: false));
            }
        }

        return control;
    }

    private static void ReadAppProperties(IArtifactSource source, string artifactPath, CanvasApp app)
    {
        const string entry = "Properties.json";
        if (!source.Has(entry))
            return;

        try
        {
            using var doc = JsonDocument.Parse(source.ReadText(entry));
            foreach (var name in new[] { "OnStart", "OnError", "StartScreen" })
            {
                var script = GetString(doc.RootElement, name);
                if (string.IsNullOrWhiteSpace(script))
                    continue;

                app.AppProperties.Add(new PowerFxProperty(
                    name,
                    script,
                    new SourceLocation(artifactPath, entry, $"App.{name}", 0, 0)));
            }
        }
        catch (JsonException)
        {
            // Properties.json ilegível não impede a análise dos controles.
        }
    }

    private static void ReadDataSources(IArtifactSource source, string artifactPath, CanvasApp app)
    {
        const string entry = "DataSources/DataSources.json";
        if (!source.Has(entry))
            return;

        try
        {
            using var doc = JsonDocument.Parse(source.ReadText(entry));
            if (!doc.RootElement.TryGetProperty("DataSources", out var list) || list.ValueKind != JsonValueKind.Array)
                return;

            foreach (var ds in list.EnumerateArray())
            {
                var name = GetString(ds, "Name");
                if (name is null)
                    continue;

                var kind = GetString(ds, "Type") ?? string.Empty;
                var columns = new List<string>();

                if (ds.TryGetProperty("Columns", out var cols) && cols.ValueKind == JsonValueKind.Array)
                {
                    foreach (var col in cols.EnumerateArray())
                    {
                        var colName = GetString(col, "Name");
                        if (colName is not null)
                            columns.Add(colName);
                    }
                }

                app.DataSources.Add(new DataSource(name, kind, columns));
            }
        }
        catch (JsonException)
        {
            // Sem data sources legíveis, as regras que dependem delas simplesmente não avaliam nada.
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
```

- [ ] **Step 4: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter MsappExtractorTests`
Expected: PASSA — 9 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: extração de canvas apps a partir do .msapp cru"
```

---

### Task 6: FlowExtractor

**Files:**
- Create: `src/PpLint.Extractors/FlowExtractor.cs`
- Test: `tests/PpLint.Extractors.Tests/FlowExtractorTests.cs`

**Interfaces:**
- Consumes: `CloudFlow`, `FlowAction`, `FlowTrigger`, `FlowVariable`, `SourceLocation`.
- Produces: `static CloudFlow? FlowExtractor.Extract(string json, string artifactPath, string entryPath, string flowName)` — retorna `null` se o JSON não contiver uma definição reconhecível.

**Formato de entrada.** O arquivo de um cloud flow tem a definição em `properties.definition`; alguns exports trazem `definition` na raiz. Dentro dela, `triggers` e `actions` são objetos onde **a chave é o nome da ação**. Cada ação tem `type`, `inputs` e `runAfter` (objeto cujas chaves são as ações predecessoras). Ações de controle (`Foreach`, `If`, `Scope`, `Switch`, `Until`) contêm ações filhas em `actions`, e o `If` também em `else.actions`.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Extractors.Tests/FlowExtractorTests.cs`:

```csharp
namespace PpLint.Extractors.Tests;

public class FlowExtractorTests
{
    private const string FlowJson = """
    {
      "properties": {
        "definition": {
          "triggers": {
            "Quando_um_item_e_criado": { "type": "OpenApiConnection", "inputs": {} }
          },
          "actions": {
            "Inicializar_contador": {
              "type": "InitializeVariable",
              "inputs": { "variables": [ { "name": "varContador", "type": "integer", "value": 0 } ] },
              "runAfter": {}
            },
            "Apply_to_each": {
              "type": "Foreach",
              "foreach": "@body('Get_items')?['value']",
              "runAfter": { "Inicializar_contador": [ "Succeeded" ] },
              "actions": {
                "Enviar_email": {
                  "type": "OpenApiConnection",
                  "inputs": { "parameters": { "emailMessage/Subject": "Olá @{items('Apply_to_each')?['Title']}" } },
                  "runAfter": {}
                }
              }
            }
          }
        }
      }
    }
    """;

    [Fact]
    public void Extract_ReadsTrigger()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido");
        Assert.NotNull(flow);
        Assert.Equal("Quando_um_item_e_criado", flow!.Trigger!.Name);
        Assert.Equal("OpenApiConnection", flow.Trigger.Type);
    }

    [Fact]
    public void Extract_ReadsTopLevelActions()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        Assert.Equal(["Inicializar_contador", "Apply_to_each"], flow.Actions.Select(a => a.Name));
    }

    [Fact]
    public void Extract_ReadsNestedActions()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        Assert.Equal(
            ["Inicializar_contador", "Apply_to_each", "Enviar_email"],
            flow.AllActions().Select(a => a.Name));
    }

    [Fact]
    public void Extract_ReadsRunAfter()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        var loop = flow.AllActions().Single(a => a.Name == "Apply_to_each");
        Assert.Equal(["Inicializar_contador"], loop.RunAfter);
    }

    [Fact]
    public void Extract_ReadsInitializedVariables()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        var v = Assert.Single(flow.Variables);
        Assert.Equal("varContador", v.Name);
        Assert.Equal("integer", v.Type);
    }

    [Fact]
    public void Extract_CollectsStringsFromInputsAsExpressions()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        var email = flow.AllActions().Single(a => a.Name == "Enviar_email");
        Assert.Contains(email.Expressions, e => e.Contains("items('Apply_to_each')"));
    }

    [Fact]
    public void Extract_ForeachExpressionIsCollected()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        var loop = flow.AllActions().Single(a => a.Name == "Apply_to_each");
        Assert.Contains(loop.Expressions, e => e.Contains("body('Get_items')"));
    }

    [Fact]
    public void Extract_AcceptsDefinitionAtRoot()
    {
        const string json = """
        { "definition": { "actions": { "Compose": { "type": "Compose", "inputs": "x" } } } }
        """;
        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Single(flow.Actions);
    }

    [Fact]
    public void Extract_ReadsElseBranchOfCondition()
    {
        const string json = """
        {
          "definition": {
            "actions": {
              "Condicao": {
                "type": "If",
                "actions": { "Sim": { "type": "Compose", "inputs": "a" } },
                "else": { "actions": { "Nao": { "type": "Compose", "inputs": "b" } } }
              }
            }
          }
        }
        """;
        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Equal(["Condicao", "Sim", "Nao"], flow.AllActions().Select(a => a.Name));
    }

    [Fact]
    public void Extract_ReturnsNullForUnrecognizedJson()
    {
        Assert.Null(FlowExtractor.Extract("{\"outra\":1}", "sol.zip", "Workflows/f.json", "F"));
    }

    [Fact]
    public void Extract_ReturnsNullForMalformedJson()
    {
        Assert.Null(FlowExtractor.Extract("{ nao e json", "sol.zip", "Workflows/f.json", "F"));
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter FlowExtractorTests`
Expected: FALHA de compilação — `FlowExtractor` não existe.

- [ ] **Step 3: Implementar o extractor**

`src/PpLint.Extractors/FlowExtractor.cs`:

```csharp
using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Extractors;

/// <summary>
/// Extrai o modelo de um cloud flow a partir da definição Logic Apps.
/// Em 'triggers' e 'actions', a chave do objeto é o nome da ação.
/// </summary>
public static class FlowExtractor
{
    private static readonly string[] ContainerProperties = ["actions"];

    public static CloudFlow? Extract(string json, string artifactPath, string entryPath, string flowName)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var definition = FindDefinition(doc.RootElement);
            if (definition is null)
                return null;

            var flow = new CloudFlow
            {
                Name = flowName,
                Location = new SourceLocation(artifactPath, entryPath, flowName, 0, 0),
            };

            if (definition.Value.TryGetProperty("triggers", out var triggers)
                && triggers.ValueKind == JsonValueKind.Object)
            {
                foreach (var t in triggers.EnumerateObject())
                {
                    flow.Trigger = new FlowTrigger(
                        t.Name,
                        GetString(t.Value, "type") ?? string.Empty,
                        new SourceLocation(artifactPath, entryPath, t.Name, 0, 0));
                    break; // um fluxo tem exatamente um trigger
                }
            }

            if (definition.Value.TryGetProperty("actions", out var actions)
                && actions.ValueKind == JsonValueKind.Object)
            {
                foreach (var action in ReadActions(actions, artifactPath, entryPath, flow))
                    flow.Actions.Add(action);
            }

            return flow;
        }
    }

    private static JsonElement? FindDefinition(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object
            && props.TryGetProperty("definition", out var nested)
            && nested.ValueKind == JsonValueKind.Object)
            return nested;

        if (root.TryGetProperty("definition", out var direct) && direct.ValueKind == JsonValueKind.Object)
            return direct;

        if (root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Object)
            return root;

        return null;
    }

    private static List<FlowAction> ReadActions(
        JsonElement actions, string artifactPath, string entryPath, CloudFlow flow)
    {
        var result = new List<FlowAction>();

        foreach (var property in actions.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;

            var action = new FlowAction
            {
                Name = property.Name,
                Type = GetString(property.Value, "type") ?? string.Empty,
                Location = new SourceLocation(artifactPath, entryPath, property.Name, 0, 0),
            };

            if (property.Value.TryGetProperty("runAfter", out var runAfter)
                && runAfter.ValueKind == JsonValueKind.Object)
            {
                foreach (var predecessor in runAfter.EnumerateObject())
                    action.RunAfter.Add(predecessor.Name);
            }

            foreach (var name in new[] { "inputs", "foreach", "expression", "condition" })
            {
                if (property.Value.TryGetProperty(name, out var value))
                    CollectStrings(value, action.Expressions);
            }

            CollectVariables(property.Value, action, artifactPath, entryPath, flow);

            foreach (var containerProperty in ContainerProperties)
            {
                if (property.Value.TryGetProperty(containerProperty, out var child)
                    && child.ValueKind == JsonValueKind.Object)
                {
                    action.Children.AddRange(ReadActions(child, artifactPath, entryPath, flow));
                }
            }

            if (property.Value.TryGetProperty("else", out var elseBranch)
                && elseBranch.ValueKind == JsonValueKind.Object
                && elseBranch.TryGetProperty("actions", out var elseActions)
                && elseActions.ValueKind == JsonValueKind.Object)
            {
                action.Children.AddRange(ReadActions(elseActions, artifactPath, entryPath, flow));
            }

            result.Add(action);
        }

        return result;
    }

    private static void CollectVariables(
        JsonElement actionElement, FlowAction action, string artifactPath, string entryPath, CloudFlow flow)
    {
        if (!action.Type.Equals("InitializeVariable", StringComparison.OrdinalIgnoreCase))
            return;

        if (!actionElement.TryGetProperty("inputs", out var inputs)
            || inputs.ValueKind != JsonValueKind.Object
            || !inputs.TryGetProperty("variables", out var variables)
            || variables.ValueKind != JsonValueKind.Array)
            return;

        foreach (var v in variables.EnumerateArray())
        {
            var name = GetString(v, "name");
            if (name is null)
                continue;

            flow.Variables.Add(new FlowVariable(
                name,
                GetString(v, "type") ?? string.Empty,
                new SourceLocation(artifactPath, entryPath, action.Name, 0, 0)));
        }
    }

    /// <summary>Coleta recursivamente toda string do elemento — é onde vivem as expressões @{...}.</summary>
    private static void CollectStrings(JsonElement element, List<string> target)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrEmpty(s))
                    target.Add(s);
                break;
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                    CollectStrings(p.Value, target);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectStrings(item, target);
                break;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
```

- [ ] **Step 4: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter FlowExtractorTests`
Expected: PASSA — 11 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: extração de cloud flows da definição Logic Apps"
```

---

### Task 7: SolutionExtractor e ProjectLoader

**Files:**
- Create: `src/PpLint.Extractors/SolutionExtractor.cs`, `src/PpLint.Extractors/ProjectLoader.cs`
- Test: `tests/PpLint.Extractors.Tests/SolutionExtractorTests.cs`, `tests/PpLint.Extractors.Tests/ProjectLoaderTests.cs`

**Interfaces:**
- Consumes: `IArtifactSource`, `MsappExtractor`, `FlowExtractor`, `PowerPlatformProject`, `SolutionInfo`, `DataTable`, `DataColumn`.
- Produces: `static void SolutionExtractor.Populate(IArtifactSource source, PowerPlatformProject project)`; `static PowerPlatformProject ProjectLoader.Load(string path)`.

**Formato de entrada.** `solution.xml` traz `ImportExportXml/SolutionManifest` com `UniqueName`, `Version`, `Managed` e `Publisher/CustomizationPrefix`. Cada `Entities/<nome>/Entity.xml` tem `EntityInfo/entity/attributes/attribute`, onde o nome lógico está no filho `LogicalName` (ou `Name`) e o schema name no atributo `PhysicalName`. Apps ficam em `CanvasApps/*.msapp`; fluxos em `Workflows/<Nome>-<GUID>.json`.

- [ ] **Step 1: Escrever o teste do SolutionExtractor**

`tests/PpLint.Extractors.Tests/SolutionExtractorTests.cs`:

```csharp
using PpLint.Core.Model;

namespace PpLint.Extractors.Tests;

public class SolutionExtractorTests
{
    private const string SolutionXml = """
    <?xml version="1.0" encoding="utf-8"?>
    <ImportExportXml version="9.2.0.0">
      <SolutionManifest>
        <UniqueName>MinhaSolucao</UniqueName>
        <Version>1.0.0.3</Version>
        <Managed>0</Managed>
        <Publisher>
          <UniqueName>contoso</UniqueName>
          <CustomizationPrefix>cts</CustomizationPrefix>
        </Publisher>
      </SolutionManifest>
    </ImportExportXml>
    """;

    private const string EntityXml = """
    <?xml version="1.0" encoding="utf-8"?>
    <Entity>
      <Name LocalizedName="Pedido">cts_pedido</Name>
      <EntityInfo>
        <entity Name="cts_pedido">
          <attributes>
            <attribute PhysicalName="cts_Titulo">
              <Type>nvarchar</Type>
              <LogicalName>cts_titulo</LogicalName>
              <RequiredLevel>required</RequiredLevel>
            </attribute>
            <attribute PhysicalName="ValorTotal">
              <Type>money</Type>
              <LogicalName>valortotal</LogicalName>
              <RequiredLevel>none</RequiredLevel>
            </attribute>
          </attributes>
        </entity>
      </EntityInfo>
    </Entity>
    """;

    private const string FlowJson = """
    { "definition": { "actions": { "Compose": { "type": "Compose", "inputs": "x" } } } }
    """;

    [Fact]
    public void Populate_ReadsSolutionManifest()
    {
        var zip = TestZip.Create(("solution.xml", SolutionXml));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        Assert.NotNull(project.Solution);
        Assert.Equal("MinhaSolucao", project.Solution!.UniqueName);
        Assert.Equal("cts", project.Solution.PublisherPrefix);
        Assert.Equal("1.0.0.3", project.Solution.Version);
        Assert.False(project.Solution.Managed);
    }

    [Fact]
    public void Populate_ReadsManagedFlag()
    {
        var zip = TestZip.Create(("solution.xml", SolutionXml.Replace("<Managed>0</Managed>", "<Managed>1</Managed>")));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        Assert.True(project.Solution!.Managed);
    }

    [Fact]
    public void Populate_ReadsEntityColumns()
    {
        var zip = TestZip.Create(("solution.xml", SolutionXml), ("Entities/cts_pedido/Entity.xml", EntityXml));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        var table = Assert.Single(project.Tables);
        Assert.Equal("cts_pedido", table.LogicalName);
        Assert.Equal(2, table.Columns.Count);

        var titulo = table.Columns.Single(c => c.LogicalName == "cts_titulo");
        Assert.Equal("cts_Titulo", titulo.SchemaName);
        Assert.Equal("nvarchar", titulo.Type);
        Assert.True(titulo.Required);

        Assert.False(table.Columns.Single(c => c.LogicalName == "valortotal").Required);
    }

    [Fact]
    public void Populate_ReadsWorkflowsAndDerivesNameFromFileName()
    {
        var zip = TestZip.Create(
            ("solution.xml", SolutionXml),
            ("Workflows/AprovarPedido-A1B2C3D4-1111-2222-3333-444455556666.json", FlowJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        var flow = Assert.Single(project.Flows);
        Assert.Equal("AprovarPedido", flow.Name);
    }

    [Fact]
    public void Populate_ReadsNestedCanvasApp()
    {
        var msapp = TestZip.Create(("Controls/1.json",
            """{ "TopParent": { "Name": "scrHome", "Template": { "Name": "screen" }, "Children": [] } }"""));
        var zip = TestZip.CreateNested("CanvasApps/AppVendas_DocumentUri.msapp", msapp, ("solution.xml", SolutionXml));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        var app = Assert.Single(project.Apps);
        Assert.Equal("AppVendas", app.Name);
        Assert.Single(app.Screens);
    }

    [Fact]
    public void Populate_MalformedEntityXmlIsSkipped()
    {
        var zip = TestZip.Create(("solution.xml", SolutionXml), ("Entities/x/Entity.xml", "<nao fechado"));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        Assert.Empty(project.Tables);
        Assert.NotNull(project.Solution);
    }

    [Fact]
    public void Populate_WithoutSolutionXmlLeavesSolutionNull()
    {
        var zip = TestZip.Create(("Workflows/F-11111111-1111-1111-1111-111111111111.json", FlowJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        Assert.Null(project.Solution);
        Assert.Single(project.Flows);
    }
}
```

- [ ] **Step 2: Escrever o teste do ProjectLoader**

`tests/PpLint.Extractors.Tests/ProjectLoaderTests.cs`:

```csharp
namespace PpLint.Extractors.Tests;

public class ProjectLoaderTests
{
    private const string ControlsJson =
        """{ "TopParent": { "Name": "scrHome", "Template": { "Name": "screen" }, "Children": [] } }""";

    [Fact]
    public void Load_MsappFileProducesSingleApp()
    {
        var msapp = TestZip.Create(("Controls/1.json", ControlsJson));
        var renamed = Path.ChangeExtension(msapp, ".msapp");
        File.Move(msapp, renamed);

        var project = ProjectLoader.Load(renamed);

        var app = Assert.Single(project.Apps);
        Assert.Single(app.Screens);
        Assert.Null(project.Solution);
    }

    [Fact]
    public void Load_SolutionZipProducesSolutionInfo()
    {
        const string solutionXml = """
        <ImportExportXml><SolutionManifest><UniqueName>S</UniqueName><Version>1.0</Version>
        <Managed>0</Managed><Publisher><CustomizationPrefix>abc</CustomizationPrefix></Publisher>
        </SolutionManifest></ImportExportXml>
        """;
        var zip = TestZip.Create(("solution.xml", solutionXml));
        var project = ProjectLoader.Load(zip);

        Assert.Equal("abc", project.Solution!.PublisherPrefix);
    }

    [Fact]
    public void Load_SetsSourcePath()
    {
        var zip = TestZip.Create(("solution.xml", "<ImportExportXml><SolutionManifest/></ImportExportXml>"));
        Assert.Equal(zip, ProjectLoader.Load(zip).SourcePath);
    }

    [Fact]
    public void Load_MissingPathThrowsArtifactException()
    {
        Assert.Throws<ArtifactException>(() => ProjectLoader.Load("nao-existe.zip"));
    }
}
```

- [ ] **Step 3: Rodar os testes e confirmar que falham**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter "SolutionExtractorTests|ProjectLoaderTests"`
Expected: FALHA de compilação — `SolutionExtractor` e `ProjectLoader` não existem.

- [ ] **Step 4: Implementar o SolutionExtractor**

`src/PpLint.Extractors/SolutionExtractor.cs`:

```csharp
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Extractors;

/// <summary>
/// Preenche o projeto a partir de uma solução exportada: manifesto,
/// tabelas Dataverse, canvas apps aninhados e cloud flows.
/// </summary>
public static class SolutionExtractor
{
    private static readonly Regex GuidSuffix = new(
        @"-[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$",
        RegexOptions.Compiled);

    public static void Populate(IArtifactSource source, PowerPlatformProject project)
    {
        ReadManifest(source, project);
        ReadEntities(source, project);
        ReadCanvasApps(source, project);
        ReadWorkflows(source, project);
    }

    private static void ReadManifest(IArtifactSource source, PowerPlatformProject project)
    {
        if (!source.Has("solution.xml"))
            return;

        var manifest = TryParse(source, "solution.xml")?.Root?.Element("SolutionManifest");
        if (manifest is null)
            return;

        project.Solution = new SolutionInfo(
            UniqueName: manifest.Element("UniqueName")?.Value ?? string.Empty,
            PublisherPrefix: manifest.Element("Publisher")?.Element("CustomizationPrefix")?.Value ?? string.Empty,
            Version: manifest.Element("Version")?.Value ?? string.Empty,
            Managed: manifest.Element("Managed")?.Value.Trim() == "1");
    }

    private static void ReadEntities(IArtifactSource source, PowerPlatformProject project)
    {
        var entries = source.Entries
            .Where(e => e.StartsWith("Entities/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith("/Entity.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var doc = TryParse(source, entry);
            var entity = doc?.Root?.Element("EntityInfo")?.Element("entity");
            if (entity is null)
                continue;

            var logicalName = entity.Attribute("Name")?.Value
                              ?? doc!.Root!.Element("Name")?.Value
                              ?? string.Empty;

            var columns = new List<DataColumn>();
            foreach (var attribute in entity.Element("attributes")?.Elements("attribute") ?? [])
            {
                var columnLogical = attribute.Element("LogicalName")?.Value
                                    ?? attribute.Element("Name")?.Value;
                if (string.IsNullOrWhiteSpace(columnLogical))
                    continue;

                columns.Add(new DataColumn(
                    LogicalName: columnLogical,
                    SchemaName: attribute.Attribute("PhysicalName")?.Value ?? columnLogical,
                    Type: attribute.Element("Type")?.Value ?? string.Empty,
                    Required: attribute.Element("RequiredLevel")?.Value
                        .Equals("required", StringComparison.OrdinalIgnoreCase) == true));
            }

            project.Tables.Add(new DataTable(
                logicalName,
                logicalName,
                columns,
                new SourceLocation(project.SourcePath, entry, logicalName, 0, 0)));
        }
    }

    private static void ReadCanvasApps(IArtifactSource source, PowerPlatformProject project)
    {
        var entries = source.Entries
            .Where(e => e.EndsWith(".msapp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var nested = source.OpenNested(entry);
            if (nested is null)
                continue;

            project.Apps.Add(MsappExtractor.Extract(nested, project.SourcePath, AppNameFrom(entry)));
        }
    }

    private static void ReadWorkflows(IArtifactSource source, PowerPlatformProject project)
    {
        var entries = source.Entries
            .Where(e => e.StartsWith("Workflows/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            string json;
            try
            {
                json = source.ReadText(entry);
            }
            catch (ArtifactException)
            {
                continue;
            }

            var flow = FlowExtractor.Extract(json, project.SourcePath, entry, FlowNameFrom(entry));
            if (flow is not null)
                project.Flows.Add(flow);
        }
    }

    /// <summary>"CanvasApps/AppVendas_DocumentUri.msapp" -> "AppVendas".</summary>
    private static string AppNameFrom(string entry)
    {
        var name = Path.GetFileNameWithoutExtension(entry);
        const string suffix = "_DocumentUri";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : name;
    }

    /// <summary>"Workflows/AprovarPedido-{GUID}.json" -> "AprovarPedido".</summary>
    private static string FlowNameFrom(string entry) =>
        GuidSuffix.Replace(Path.GetFileNameWithoutExtension(entry), string.Empty);

    private static XDocument? TryParse(IArtifactSource source, string entry)
    {
        try
        {
            return XDocument.Parse(source.ReadText(entry));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        catch (ArtifactException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Implementar o ProjectLoader**

`src/PpLint.Extractors/ProjectLoader.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Extractors;

/// <summary>
/// Ponto de entrada da extração: recebe o caminho passado na linha de
/// comando e devolve o IR completo. Decide sozinho se o artefato é uma
/// solução, um app isolado ou uma pasta descompactada.
/// </summary>
public static class ProjectLoader
{
    public static PowerPlatformProject Load(string path)
    {
        using var source = ArtifactSourceFactory.Open(path);

        var project = new PowerPlatformProject { SourcePath = path };

        if (IsCanvasAppPackage(path, source))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            project.Apps.Add(MsappExtractor.Extract(source, path, name));
            return project;
        }

        SolutionExtractor.Populate(source, project);
        return project;
    }

    /// <summary>
    /// Um .msapp isolado tem Controls/ na raiz e não tem solution.xml.
    /// A extensão sozinha não basta: pastas descompactadas também precisam ser reconhecidas.
    /// </summary>
    private static bool IsCanvasAppPackage(string path, IArtifactSource source)
    {
        if (source.Has("solution.xml"))
            return false;

        if (path.EndsWith(".msapp", StringComparison.OrdinalIgnoreCase))
            return true;

        return source.Entries.Any(e =>
            e.StartsWith("Controls/", StringComparison.OrdinalIgnoreCase)
            && e.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 6: Rodar os testes e confirmar que passam**

Run: `dotnet test tests/PpLint.Extractors.Tests`
Expected: PASSA — todos os testes de `PpLint.Extractors.Tests`, incluindo os 7 de `SolutionExtractorTests` e os 4 de `ProjectLoaderTests`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: extração de solução exportada e carregamento do projeto"
```

---

### Task 8: Parser de Power Fx e percurso de AST

**Files:**
- Create: `src/PpLint.PowerFx/PpLint.PowerFx.csproj`, `src/PpLint.PowerFx/PowerFxParser.cs`, `src/PpLint.PowerFx/AstWalker.cs`
- Test: `tests/PpLint.PowerFx.Tests/PpLint.PowerFx.Tests.csproj`, `tests/PpLint.PowerFx.Tests/PowerFxParserTests.cs`

**Interfaces:**
- Consumes: nada das tasks anteriores.
- Produces:
  - `sealed record FxParseResult(bool IsSuccess, TexlNode? Root, IReadOnlyList<string> Errors)`.
  - `static FxParseResult PowerFxParser.Parse(string script)` — nunca lança; erro de sintaxe vira `IsSuccess = false`.
  - `static IEnumerable<TexlNode> AstWalker.Descendants(TexlNode root)` — todos os nós, incluindo a raiz.
  - `static IEnumerable<CallNode> AstWalker.Calls(TexlNode root, string functionName)` — chamadas de uma função, comparação sem diferenciar maiúsculas.
  - `static IEnumerable<FirstNameNode> AstWalker.Identifiers(TexlNode root)`.
  - `static string? AstWalker.FunctionName(CallNode call)`.

> **Ponto de incerteza de API — leia antes de implementar.** Os nomes exatos de tipos do `Microsoft.PowerFx.Core` (`Engine`, `ParseResult`, `TexlNode`, `CallNode`, `FirstNameNode`, `TexlVisitor`) e a forma de percorrer a árvore variam entre versões maiores do pacote. O Step 2 abaixo é um spike de verificação que resolve isso empiricamente antes de qualquer implementação. Se um nome divergir, ajuste o código desta task e mantenha as **assinaturas de `PowerFxParser` e `AstWalker` exatamente como especificado acima** — as tasks 12 e 14 dependem delas e não podem mudar.

- [ ] **Step 1: Criar o projeto e instalar o pacote**

```bash
cd /c/PROJETOS/pp-lint
dotnet new classlib -o src/PpLint.PowerFx -f net10.0
dotnet new xunit -o tests/PpLint.PowerFx.Tests -f net10.0
rm src/PpLint.PowerFx/Class1.cs tests/PpLint.PowerFx.Tests/UnitTest1.cs
dotnet sln add src/PpLint.PowerFx/PpLint.PowerFx.csproj tests/PpLint.PowerFx.Tests/PpLint.PowerFx.Tests.csproj
dotnet add src/PpLint.PowerFx reference src/PpLint.Core
dotnet add src/PpLint.PowerFx package Microsoft.PowerFx.Core
dotnet add tests/PpLint.PowerFx.Tests reference src/PpLint.PowerFx
```

- [ ] **Step 2: Spike de verificação da API (descartável)**

Criar `tests/PpLint.PowerFx.Tests/ApiSpike.cs`, rodar, ler a saída e **apagar o arquivo antes do commit**:

```csharp
using Microsoft.PowerFx;
using Xunit.Abstractions;

namespace PpLint.PowerFx.Tests;

public class ApiSpike(ITestOutputHelper output)
{
    [Fact]
    public void PrintParseShape()
    {
        var engine = new Engine(new PowerFxConfig());
        var result = engine.Parse("Set(varTotal, 1 + 2)");

        output.WriteLine($"ParseResult: {result.GetType().FullName}");
        output.WriteLine($"IsSuccess: {result.IsSuccess}");
        output.WriteLine($"Root: {result.Root?.GetType().FullName}");
        output.WriteLine($"Root.Kind: {result.Root?.Kind}");

        foreach (var m in result.Root!.GetType().GetMethods().Select(m => m.Name).Distinct().Order())
            output.WriteLine($"método: {m}");
    }
}
```

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter PrintParseShape --logger "console;verbosity=detailed"`

Confirme antes de seguir: (a) `Engine.Parse(string)` existe e devolve algo com `IsSuccess`, `Root` e `Errors`; (b) `Root` é um `TexlNode` do namespace `Microsoft.PowerFx.Syntax`; (c) existe um mecanismo de visitor (`Accept(TexlVisitor)`) ou de acesso a filhos. Se o percurso por visitor não estiver disponível publicamente, use a alternativa documentada no Step 5.

- [ ] **Step 3: Escrever o teste que falha**

`tests/PpLint.PowerFx.Tests/PowerFxParserTests.cs`:

```csharp
using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx.Tests;

public class PowerFxParserTests
{
    [Fact]
    public void Parse_ValidExpressionSucceeds()
    {
        var result = PowerFxParser.Parse("Set(varTotal, 1 + 2)");
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Root);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_SyntaxErrorFailsWithoutThrowing()
    {
        var result = PowerFxParser.Parse("Set(varTotal, ");
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_EmptyScriptSucceedsWithNoRoot()
    {
        var result = PowerFxParser.Parse("   ");
        Assert.True(result.IsSuccess);
        Assert.Null(result.Root);
    }

    [Fact]
    public void Calls_FindsFunctionByNameCaseInsensitively()
    {
        var root = PowerFxParser.Parse("Set(varA, 1); set(varB, 2)").Root!;
        Assert.Equal(2, AstWalker.Calls(root, "SET").Count());
    }

    [Fact]
    public void Calls_FindsNestedCalls()
    {
        var root = PowerFxParser.Parse("If(IsBlank(varX), Notify(\"vazio\"), Set(varY, 1))").Root!;
        Assert.Single(AstWalker.Calls(root, "Notify"));
        Assert.Single(AstWalker.Calls(root, "IsBlank"));
        Assert.Single(AstWalker.Calls(root, "Set"));
    }

    [Fact]
    public void FunctionName_ReturnsHeadName()
    {
        var root = PowerFxParser.Parse("Notify(\"oi\")").Root!;
        var call = Assert.Single(AstWalker.Calls(root, "Notify"));
        Assert.Equal("Notify", AstWalker.FunctionName(call));
    }

    [Fact]
    public void Identifiers_FindsAllFirstNames()
    {
        var root = PowerFxParser.Parse("Set(varTotal, varPreco * varQuantidade)").Root!;
        var names = AstWalker.Identifiers(root).Select(i => i.Ident.Name.Value).ToList();
        Assert.Contains("varTotal", names);
        Assert.Contains("varPreco", names);
        Assert.Contains("varQuantidade", names);
    }

    [Fact]
    public void Descendants_IncludesRootAndAllNodes()
    {
        var root = PowerFxParser.Parse("1 + 2").Root!;
        var all = AstWalker.Descendants(root).ToList();
        Assert.Contains(root, all);
        Assert.True(all.Count >= 3, "raiz mais os dois literais");
    }

    [Fact]
    public void Descendants_HandlesDeeplyNestedExpression()
    {
        var root = PowerFxParser.Parse("If(a, If(b, If(c, 1, 2), 3), 4)").Root!;
        Assert.Equal(3, AstWalker.Calls(root, "If").Count());
    }
}
```

- [ ] **Step 4: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter PowerFxParserTests`
Expected: FALHA de compilação — `PowerFxParser` e `AstWalker` não existem.

- [ ] **Step 5: Implementar o parser e o walker**

`src/PpLint.PowerFx/PowerFxParser.cs`:

```csharp
using System.Globalization;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx;

public sealed record FxParseResult(bool IsSuccess, TexlNode? Root, IReadOnlyList<string> Errors);

/// <summary>
/// Fachada sobre o parser oficial do Power Fx. Nunca lança: uma expressão
/// que não compila vira um resultado com IsSuccess = false, para que a
/// análise do restante do app continue.
/// </summary>
public static class PowerFxParser
{
    private static readonly Engine SharedEngine = new(new PowerFxConfig());

    /// <summary>
    /// Cultura invariante porque o .msapp guarda InvariantScript — vírgula separa
    /// argumentos, independentemente da cultura da máquina. AllowsSideEffects habilita
    /// o operador de encadeamento ';', usado em toda propriedade de comportamento.
    /// </summary>
    private static readonly ParserOptions Options = new()
    {
        Culture = CultureInfo.InvariantCulture,
        AllowsSideEffects = true,
    };

    public static FxParseResult Parse(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new FxParseResult(true, null, []);

        try
        {
            var result = SharedEngine.Parse(script, Options);
            var errors = result.Errors?.Select(e => e.ToString() ?? string.Empty).ToList() ?? [];

            return result.IsSuccess
                ? new FxParseResult(true, result.Root, [])
                : new FxParseResult(false, null, errors.Count > 0 ? errors : ["Erro de sintaxe."]);
        }
        catch (Exception ex)
        {
            // Defensivo: nenhuma expressão de usuário pode derrubar a análise.
            return new FxParseResult(false, null, [ex.Message]);
        }
    }
}
```

`src/PpLint.PowerFx/AstWalker.cs`:

```csharp
using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx;

/// <summary>
/// Percurso da AST do Power Fx. Usa um visitor que coleta cada nó visitado;
/// se a versão do pacote não expuser TexlVisitor publicamente, substitua a
/// implementação de Descendants por reflexão sobre os filhos, mantendo as
/// assinaturas públicas intactas.
/// </summary>
public static class AstWalker
{
    public static IEnumerable<TexlNode> Descendants(TexlNode root)
    {
        var collector = new NodeCollector();
        root.Accept(collector);
        return collector.Nodes;
    }

    public static IEnumerable<CallNode> Calls(TexlNode root, string functionName) =>
        Descendants(root)
            .OfType<CallNode>()
            .Where(c => string.Equals(FunctionName(c), functionName, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<FirstNameNode> Identifiers(TexlNode root) =>
        Descendants(root).OfType<FirstNameNode>();

    public static string? FunctionName(CallNode call) => call.Head?.Name.Value;

    private sealed class NodeCollector : IdentityTexlVisitor
    {
        public List<TexlNode> Nodes { get; } = [];

        public override void Visit(ErrorNode node) => Add(node);
        public override void Visit(BlankNode node) => Add(node);
        public override void Visit(BoolLitNode node) => Add(node);
        public override void Visit(StrLitNode node) => Add(node);
        public override void Visit(NumLitNode node) => Add(node);
        public override void Visit(DecLitNode node) => Add(node);
        public override void Visit(FirstNameNode node) => Add(node);
        public override void Visit(ParentNode node) => Add(node);
        public override void Visit(SelfNode node) => Add(node);

        public override void PostVisit(DottedNameNode node) => Add(node);
        public override void PostVisit(UnaryOpNode node) => Add(node);
        public override void PostVisit(BinaryOpNode node) => Add(node);
        public override void PostVisit(VariadicOpNode node) => Add(node);
        public override void PostVisit(CallNode node) => Add(node);
        public override void PostVisit(ListNode node) => Add(node);
        public override void PostVisit(RecordNode node) => Add(node);
        public override void PostVisit(TableNode node) => Add(node);
        public override void PostVisit(AsNode node) => Add(node);
        public override void PostVisit(StrInterpNode node) => Add(node);

        private void Add(TexlNode node) => Nodes.Add(node);
    }
}
```

> Se `IdentityTexlVisitor` não existir na versão instalada, herde de `TexlVisitor` e implemente todos os membros abstratos com o mesmo corpo `Add(node)`. Se algum tipo de nó da lista acima não existir na versão instalada, remova apenas aquele override — os testes continuam válidos.

- [ ] **Step 6: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter PowerFxParserTests`
Expected: PASSA — 9 testes.

- [ ] **Step 7: Apagar o spike e commitar**

```bash
cd /c/PROJETOS/pp-lint
rm tests/PpLint.PowerFx.Tests/ApiSpike.cs
dotnet test
git add -A
git commit -m "feat: parser de Power Fx e percurso de AST"
```

---

### Task 9: Motor de regras

**Files:**
- Create: `src/PpLint.Core/PpLintConfig.cs`, `src/PpLint.Core/Rules/RuleAttribute.cs`, `src/PpLint.Core/Rules/IRule.cs`, `src/PpLint.Core/Rules/LintContext.cs`, `src/PpLint.Core/Rules/RuleEngine.cs`, `src/PpLint.Core/Rules/LintResult.cs`
- Test: `tests/PpLint.Core.Tests/RuleEngineTests.cs`

**Interfaces:**
- Consumes: `Diagnostic`, `Severity`, `RuleCategory`, `SourceLocation`, `PowerPlatformProject`.
- Produces:
  - `[AttributeUsage(AttributeTargets.Class)] sealed class RuleAttribute(string id, RuleCategory category, Severity defaultSeverity)` com `Id`, `Category`, `DefaultSeverity`.
  - `interface IRule { void Check(LintContext ctx); }`.
  - `LintContext` com `PowerPlatformProject Project`, `PpLintConfig Config`, `void Evaluated(int count)`, `void Report(SourceLocation location, string message)`.
  - `sealed record RuleTally(string RuleId, RuleCategory Category, Severity Severity, int Evaluated, int Violations)`.
  - `sealed record LintResult(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<RuleTally> Tallies)`.
  - `RuleEngine` com `RuleEngine(IEnumerable<IRule> rules)`, `static RuleEngine CreateDefault(params System.Reflection.Assembly[] assemblies)`, `LintResult Run(PowerPlatformProject project, PpLintConfig config)`.
  - `PpLintConfig` com `IReadOnlySet<string> Ignore`, `IReadOnlyDictionary<string, Severity> SeverityOverrides`, `NamingConfig Naming`, `static PpLintConfig Default`.
  - `NamingConfig` com `string GlobalVariable`, `string ContextVariable`, `string Collection`, `string Screen`, `string Component`, `IReadOnlyDictionary<string, string> ControlPrefixes`.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/RuleEngineTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Core.Tests;

[Rule("XX001", RuleCategory.Naming, Severity.Warning)]
public sealed class AlwaysReportsRule : IRule
{
    public void Check(LintContext ctx)
    {
        ctx.Evaluated(3);
        ctx.Report(new SourceLocation("a.zip", "b.json", "alvo", 0, 0), "problema encontrado");
    }
}

[Rule("XX002", RuleCategory.PowerFx, Severity.Info)]
public sealed class NeverReportsRule : IRule
{
    public void Check(LintContext ctx) => ctx.Evaluated(5);
}

[Rule("XX003", RuleCategory.Flow, Severity.Error)]
public sealed class ThrowingRule : IRule
{
    public void Check(LintContext ctx) => throw new InvalidOperationException("regra com defeito");
}

public class RuleEngineTests
{
    private static PowerPlatformProject EmptyProject() => new() { SourcePath = "a.zip" };

    [Fact]
    public void Run_ProducesDiagnosticWithRuleMetadata()
    {
        var engine = new RuleEngine([new AlwaysReportsRule()]);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        var d = Assert.Single(result.Diagnostics);
        Assert.Equal("XX001", d.RuleId);
        Assert.Equal(RuleCategory.Naming, d.Category);
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Equal("problema encontrado", d.Message);
    }

    [Fact]
    public void Run_RecordsTalliesForEveryRule()
    {
        var engine = new RuleEngine([new AlwaysReportsRule(), new NeverReportsRule()]);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        Assert.Equal(2, result.Tallies.Count);
        var t1 = result.Tallies.Single(t => t.RuleId == "XX001");
        Assert.Equal(3, t1.Evaluated);
        Assert.Equal(1, t1.Violations);

        var t2 = result.Tallies.Single(t => t.RuleId == "XX002");
        Assert.Equal(5, t2.Evaluated);
        Assert.Equal(0, t2.Violations);
    }

    [Fact]
    public void Run_IgnoredRuleDoesNotRun()
    {
        var config = PpLintConfig.Default with { Ignore = new HashSet<string>(["XX001"]) };
        var engine = new RuleEngine([new AlwaysReportsRule(), new NeverReportsRule()]);
        var result = engine.Run(EmptyProject(), config);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["XX002"], result.Tallies.Select(t => t.RuleId));
    }

    [Fact]
    public void Run_SeverityOverrideIsApplied()
    {
        var config = PpLintConfig.Default with
        {
            SeverityOverrides = new Dictionary<string, Severity> { ["XX001"] = Severity.Error },
        };
        var engine = new RuleEngine([new AlwaysReportsRule()]);
        var result = engine.Run(EmptyProject(), config);

        Assert.Equal(Severity.Error, Assert.Single(result.Diagnostics).Severity);
        Assert.Equal(Severity.Error, Assert.Single(result.Tallies).Severity);
    }

    [Fact]
    public void Run_RuleThatThrowsDoesNotAbortTheRun()
    {
        var engine = new RuleEngine([new ThrowingRule(), new AlwaysReportsRule()]);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        Assert.Single(result.Diagnostics, d => d.RuleId == "XX001");
        Assert.DoesNotContain(result.Tallies, t => t.RuleId == "XX003");
    }

    [Fact]
    public void Run_DiagnosticsAreSortedBySeverityThenRuleId()
    {
        var config = PpLintConfig.Default with
        {
            SeverityOverrides = new Dictionary<string, Severity> { ["XX002"] = Severity.Error },
        };
        var engine = new RuleEngine([new AlwaysReportsRule(), new ReportingInfoRule()]);
        var result = engine.Run(EmptyProject(), config);

        Assert.True(result.Diagnostics[0].Severity >= result.Diagnostics[^1].Severity);
    }

    [Fact]
    public void RuleWithoutAttribute_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new RuleEngine([new UnattributedRule()]));
        Assert.Contains("UnattributedRule", ex.Message);
    }

    [Fact]
    public void CreateDefault_DiscoversAttributedRulesInAssembly()
    {
        var engine = RuleEngine.CreateDefault(typeof(AlwaysReportsRule).Assembly);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        Assert.Contains(result.Tallies, t => t.RuleId == "XX001");
        Assert.Contains(result.Tallies, t => t.RuleId == "XX002");
    }

    [Fact]
    public void DefaultConfig_HasNamingPresets()
    {
        var naming = PpLintConfig.Default.Naming;
        Assert.Equal("^var[A-Z][A-Za-z0-9]*$", naming.GlobalVariable);
        Assert.Equal("btn", naming.ControlPrefixes["button"]);
        Assert.Equal("lbl", naming.ControlPrefixes["label"]);
    }
}

[Rule("XX004", RuleCategory.PowerFx, Severity.Info)]
public sealed class ReportingInfoRule : IRule
{
    public void Check(LintContext ctx)
    {
        ctx.Evaluated(1);
        ctx.Report(new SourceLocation("a.zip", "b.json", "outro", 0, 0), "aviso informativo");
    }
}

public sealed class UnattributedRule : IRule
{
    public void Check(LintContext ctx) => ctx.Evaluated(1);
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter RuleEngineTests`
Expected: FALHA de compilação — o namespace `PpLint.Core.Rules` não existe.

- [ ] **Step 3: Implementar a configuração**

`src/PpLint.Core/PpLintConfig.cs`:

```csharp
namespace PpLint.Core;

/// <summary>
/// Convenções de nomenclatura. Os valores default são o preset embutido,
/// baseado no padrão de codificação de canvas apps da Microsoft.
/// A partir da Fase 2 estes valores passam a ser carregáveis de pp-lint.toml.
/// </summary>
public sealed record NamingConfig
{
    public string GlobalVariable { get; init; } = "^var[A-Z][A-Za-z0-9]*$";
    public string ContextVariable { get; init; } = "^loc[A-Z][A-Za-z0-9]*$";
    public string Collection { get; init; } = "^col[A-Z][A-Za-z0-9]*$";
    public string Screen { get; init; } = "^scr[A-Z][A-Za-z0-9]*$";
    public string Component { get; init; } = "^cmp[A-Z][A-Za-z0-9]*$";

    /// <summary>Nome do template do controle (minúsculo) para o prefixo esperado.</summary>
    public IReadOnlyDictionary<string, string> ControlPrefixes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["button"] = "btn",
            ["label"] = "lbl",
            ["text"] = "txt",
            ["textcanvas"] = "lbl",
            ["gallery"] = "gal",
            ["icon"] = "ico",
            ["image"] = "img",
            ["groupcontainer"] = "cnt",
            ["form"] = "frm",
            ["dropdown"] = "drp",
            ["combobox"] = "cmb",
            ["datepicker"] = "dtp",
            ["checkbox"] = "chk",
            ["toggleswitch"] = "tgl",
            ["radio"] = "rad",
            ["slider"] = "sld",
            ["htmlviewer"] = "htm",
            ["rectangle"] = "rec",
            ["timer"] = "tim",
        };
}

public sealed record PpLintConfig
{
    public IReadOnlySet<string> Ignore { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Severity> SeverityOverrides { get; init; } =
        new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase);

    public NamingConfig Naming { get; init; } = new();

    public static PpLintConfig Default { get; } = new();
}
```

- [ ] **Step 4: Implementar o contrato de regra e o contexto**

`src/PpLint.Core/Rules/RuleAttribute.cs`:

```csharp
namespace PpLint.Core.Rules;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuleAttribute(string id, RuleCategory category, Severity defaultSeverity) : Attribute
{
    public string Id { get; } = id;
    public RuleCategory Category { get; } = category;
    public Severity DefaultSeverity { get; } = defaultSeverity;
}
```

`src/PpLint.Core/Rules/IRule.cs`:

```csharp
namespace PpLint.Core.Rules;

/// <summary>
/// Uma verificação. Toda implementação deve declarar [Rule] e chamar
/// ctx.Evaluated para cada alvo examinado, mesmo quando não reporta nada —
/// sem isso o índice de conformidade fica incorreto.
/// </summary>
public interface IRule
{
    void Check(LintContext ctx);
}
```

`src/PpLint.Core/Rules/LintContext.cs`:

```csharp
using PpLint.Core.Model;

namespace PpLint.Core.Rules;

public sealed class LintContext
{
    private readonly string _ruleId;
    private readonly RuleCategory _category;
    private readonly Severity _severity;
    private readonly List<Diagnostic> _diagnostics;

    internal LintContext(
        string ruleId,
        RuleCategory category,
        Severity severity,
        PowerPlatformProject project,
        PpLintConfig config,
        List<Diagnostic> diagnostics)
    {
        _ruleId = ruleId;
        _category = category;
        _severity = severity;
        _diagnostics = diagnostics;
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
        _diagnostics.Add(new Diagnostic(_ruleId, _category, _severity, message, location));
        ViolationCount++;
    }
}
```

`src/PpLint.Core/Rules/LintResult.cs`:

```csharp
namespace PpLint.Core.Rules;

public sealed record RuleTally(
    string RuleId,
    RuleCategory Category,
    Severity Severity,
    int Evaluated,
    int Violations);

public sealed record LintResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<RuleTally> Tallies);
```

- [ ] **Step 5: Implementar o motor**

`src/PpLint.Core/Rules/RuleEngine.cs`:

```csharp
using System.Reflection;
using PpLint.Core.Model;

namespace PpLint.Core.Rules;

public sealed class RuleEngine
{
    private readonly List<(IRule Rule, RuleAttribute Meta)> _rules = [];

    public RuleEngine(IEnumerable<IRule> rules)
    {
        foreach (var rule in rules)
        {
            var meta = rule.GetType().GetCustomAttribute<RuleAttribute>()
                ?? throw new InvalidOperationException(
                    $"A regra {rule.GetType().Name} não declara o atributo [Rule].");
            _rules.Add((rule, meta));
        }
    }

    /// <summary>Descobre por reflexão toda classe concreta com [Rule] nos assemblies informados.</summary>
    public static RuleEngine CreateDefault(params Assembly[] assemblies)
    {
        var rules = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IRule).IsAssignableFrom(t)
                        && t.GetCustomAttribute<RuleAttribute>() is not null
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IRule)Activator.CreateInstance(t)!)
            .ToList();

        return new RuleEngine(rules);
    }

    public LintResult Run(PowerPlatformProject project, PpLintConfig config)
    {
        var diagnostics = new List<Diagnostic>();
        var tallies = new List<RuleTally>();

        foreach (var (rule, meta) in _rules)
        {
            if (config.Ignore.Contains(meta.Id))
                continue;

            var severity = config.SeverityOverrides.TryGetValue(meta.Id, out var overridden)
                ? overridden
                : meta.DefaultSeverity;

            var ctx = new LintContext(meta.Id, meta.Category, severity, project, config, diagnostics);
            var before = diagnostics.Count;

            try
            {
                rule.Check(ctx);
            }
            catch (Exception)
            {
                // Uma regra com defeito não pode invalidar a execução inteira:
                // descartamos o que ela produziu e seguimos para a próxima.
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
}
```

- [ ] **Step 6: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Core.Tests --filter RuleEngineTests`
Expected: PASSA — 9 testes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: motor de regras com contagem de alvos avaliados"
```

---

### Task 10: Índice de conformidade

**Files:**
- Create: `src/PpLint.Core/Scoring/ComplianceScorer.cs`
- Test: `tests/PpLint.Core.Tests/ComplianceScorerTests.cs`

**Interfaces:**
- Consumes: `RuleTally`, `Severity`, `SeverityWeights`, `RuleCategory`.
- Produces:
  - `sealed record ComplianceScore(double Percent, int EvaluatedTargets, int Violations)`.
  - `sealed record ComplianceReport(ComplianceScore Overall, IReadOnlyDictionary<RuleCategory, ComplianceScore> ByCategory)`.
  - `static ComplianceReport ComplianceScorer.Compute(IReadOnlyList<RuleTally> tallies)`.

**Fórmula.** `Conformidade = 100 × (1 − Σ w(r)·V(r) / Σ w(r)·E(r))`, com `w` = 10/3/1 por severidade. Quando o denominador é zero (nada foi avaliado), a conformidade é 100 — não há como estar não-conforme sem alvo. Categorias sem nenhum alvo avaliado não aparecem em `ByCategory`.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Core.Tests/ComplianceScorerTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;

namespace PpLint.Core.Tests;

public class ComplianceScorerTests
{
    private static RuleTally Tally(string id, RuleCategory cat, Severity sev, int evaluated, int violations)
        => new(id, cat, sev, evaluated, violations);

    [Fact]
    public void Compute_NoViolationsIsHundredPercent()
    {
        var report = ComplianceScorer.Compute([Tally("A", RuleCategory.Naming, Severity.Warning, 100, 0)]);
        Assert.Equal(100.0, report.Overall.Percent, 4);
        Assert.Equal(100, report.Overall.EvaluatedTargets);
        Assert.Equal(0, report.Overall.Violations);
    }

    [Fact]
    public void Compute_AllTargetsViolatingIsZeroPercent()
    {
        var report = ComplianceScorer.Compute([Tally("A", RuleCategory.Naming, Severity.Warning, 10, 10)]);
        Assert.Equal(0.0, report.Overall.Percent, 4);
    }

    [Fact]
    public void Compute_SingleRuleUsesSimpleRatio()
    {
        // 46 de 358 reprovados, peso irrelevante quando há uma só regra.
        var report = ComplianceScorer.Compute([Tally("NM011", RuleCategory.Naming, Severity.Warning, 358, 46)]);
        Assert.Equal(100.0 * (1 - 46.0 / 358.0), report.Overall.Percent, 4);
    }

    [Fact]
    public void Compute_WeightsErrorsTenTimesMoreThanInfos()
    {
        // Error: 1 violação em 10 alvos -> 10*1 / 10*10
        // Info:  1 violação em 10 alvos ->  1*1 / 1*10
        // Total: (10 + 1) / (100 + 10) = 11/110 = 0,1
        var report = ComplianceScorer.Compute([
            Tally("E", RuleCategory.PowerFx, Severity.Error, 10, 1),
            Tally("I", RuleCategory.PowerFx, Severity.Info, 10, 1),
        ]);
        Assert.Equal(90.0, report.Overall.Percent, 4);
    }

    [Fact]
    public void Compute_ZeroEvaluatedIsHundredPercent()
    {
        var report = ComplianceScorer.Compute([Tally("A", RuleCategory.Flow, Severity.Error, 0, 0)]);
        Assert.Equal(100.0, report.Overall.Percent, 4);
    }

    [Fact]
    public void Compute_EmptyTalliesIsHundredPercent()
    {
        var report = ComplianceScorer.Compute([]);
        Assert.Equal(100.0, report.Overall.Percent, 4);
        Assert.Empty(report.ByCategory);
    }

    [Fact]
    public void Compute_GroupsByCategory()
    {
        var report = ComplianceScorer.Compute([
            Tally("NM1", RuleCategory.Naming, Severity.Warning, 10, 5),
            Tally("NM2", RuleCategory.Naming, Severity.Warning, 10, 0),
            Tally("PF1", RuleCategory.PowerFx, Severity.Warning, 20, 0),
        ]);

        Assert.Equal(2, report.ByCategory.Count);
        Assert.Equal(75.0, report.ByCategory[RuleCategory.Naming].Percent, 4);
        Assert.Equal(100.0, report.ByCategory[RuleCategory.PowerFx].Percent, 4);
        Assert.Equal(20, report.ByCategory[RuleCategory.Naming].EvaluatedTargets);
        Assert.Equal(5, report.ByCategory[RuleCategory.Naming].Violations);
    }

    [Fact]
    public void Compute_CategoryWithNoEvaluatedTargetsIsOmitted()
    {
        var report = ComplianceScorer.Compute([
            Tally("NM1", RuleCategory.Naming, Severity.Warning, 10, 0),
            Tally("SEC1", RuleCategory.Security, Severity.Error, 0, 0),
        ]);

        Assert.True(report.ByCategory.ContainsKey(RuleCategory.Naming));
        Assert.False(report.ByCategory.ContainsKey(RuleCategory.Security));
    }

    [Fact]
    public void Compute_ResultIsAlwaysWithinBounds()
    {
        var report = ComplianceScorer.Compute([
            Tally("A", RuleCategory.Naming, Severity.Error, 1, 1),
            Tally("B", RuleCategory.PowerFx, Severity.Info, 1, 0),
        ]);

        Assert.InRange(report.Overall.Percent, 0.0, 100.0);
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Core.Tests --filter ComplianceScorerTests`
Expected: FALHA de compilação — `ComplianceScorer` não existe.

- [ ] **Step 3: Implementar o cálculo**

`src/PpLint.Core/Scoring/ComplianceScorer.cs`:

```csharp
using PpLint.Core.Rules;

namespace PpLint.Core.Scoring;

public sealed record ComplianceScore(double Percent, int EvaluatedTargets, int Violations);

public sealed record ComplianceReport(
    ComplianceScore Overall,
    IReadOnlyDictionary<RuleCategory, ComplianceScore> ByCategory);

/// <summary>
/// Índice de conformidade: proporção de checagens aprovadas sobre checagens
/// realizadas, ponderada por severidade. Cada regra contribui com o número de
/// alvos que examinou (denominador) e de violações que encontrou (numerador).
/// </summary>
public static class ComplianceScorer
{
    public static ComplianceReport Compute(IReadOnlyList<RuleTally> tallies)
    {
        var byCategory = tallies
            .GroupBy(t => t.Category)
            .Select(g => (Category: g.Key, Score: ScoreOf(g)))
            .Where(x => x.Score.EvaluatedTargets > 0)
            .ToDictionary(x => x.Category, x => x.Score);

        return new ComplianceReport(ScoreOf(tallies), byCategory);
    }

    private static ComplianceScore ScoreOf(IEnumerable<RuleTally> tallies)
    {
        double weightedViolations = 0;
        double weightedTargets = 0;
        var targets = 0;
        var violations = 0;

        foreach (var t in tallies)
        {
            var weight = SeverityWeights.Of(t.Severity);
            weightedViolations += weight * t.Violations;
            weightedTargets += weight * t.Evaluated;
            targets += t.Evaluated;
            violations += t.Violations;
        }

        // Sem alvos avaliados não há como estar não-conforme.
        var percent = weightedTargets <= 0
            ? 100.0
            : 100.0 * (1.0 - weightedViolations / weightedTargets);

        return new ComplianceScore(Math.Clamp(percent, 0.0, 100.0), targets, violations);
    }
}
```

- [ ] **Step 4: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Core.Tests --filter ComplianceScorerTests`
Expected: PASSA — 9 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: cálculo do índice de conformidade"
```

---

### Task 11: Regras NM010 e NM011 (nomenclatura de controles)

**Files:**
- Create: `src/PpLint.Rules/PpLint.Rules.csproj`, `src/PpLint.Rules/Naming/DefaultControlNameRule.cs`, `src/PpLint.Rules/Naming/ControlPrefixRule.cs`
- Test: `tests/PpLint.Rules.Tests/PpLint.Rules.Tests.csproj`, `tests/PpLint.Rules.Tests/RuleTestHarness.cs`, `tests/PpLint.Rules.Tests/NamingRuleTests.cs`

**Interfaces:**
- Consumes: `IRule`, `RuleAttribute`, `LintContext`, `RuleEngine`, `PpLintConfig`, `Control`, `CanvasApp`, `PowerPlatformProject`.
- Produces: `DefaultControlNameRule` (NM010, Naming, Error); `ControlPrefixRule` (NM011, Naming, Warning). Ambas sem construtor com parâmetros, para serem descobertas por `RuleEngine.CreateDefault`.
- Produces (teste): `RuleTestHarness.Run(IRule rule, PowerPlatformProject project, PpLintConfig? config = null) → LintResult` e os builders `RuleTestHarness.App(string name, params Control[] screens)`, `RuleTestHarness.Screen(string name, params Control[] children)`, `RuleTestHarness.Ctl(string name, string template, params (string Property, string Script)[] properties)`, `RuleTestHarness.ProjectWith(params CanvasApp[] apps)`, `RuleTestHarness.ProjectWithFlows(params CloudFlow[] flows)`.

**Regra NM010.** Um controle tem nome default quando o nome é o nome do template seguido só de dígitos, comparado sem diferenciar maiúsculas: `Button1`, `Label12`, `Screen1`, `Gallery2`. Alvos avaliados: todos os controles de todos os apps.

**Regra NM011.** O nome do controle deve começar com o prefixo esperado para seu template, conforme `Config.Naming.ControlPrefixes`. Telas são ignoradas por esta regra — quem cuida delas é NM012, na Fase 2. Templates sem prefixo configurado não são avaliados (não entram no denominador). Controles com nome default também não são avaliados aqui, para que um mesmo problema não seja penalizado duas vezes no índice.

- [ ] **Step 1: Criar o projeto**

```bash
cd /c/PROJETOS/pp-lint
dotnet new classlib -o src/PpLint.Rules -f net10.0
dotnet new xunit -o tests/PpLint.Rules.Tests -f net10.0
rm src/PpLint.Rules/Class1.cs tests/PpLint.Rules.Tests/UnitTest1.cs
dotnet sln add src/PpLint.Rules/PpLint.Rules.csproj tests/PpLint.Rules.Tests/PpLint.Rules.Tests.csproj
dotnet add src/PpLint.Rules reference src/PpLint.Core
dotnet add src/PpLint.Rules reference src/PpLint.PowerFx
dotnet add tests/PpLint.Rules.Tests reference src/PpLint.Rules
```

- [ ] **Step 2: Escrever o harness de teste**

`tests/PpLint.Rules.Tests/RuleTestHarness.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Tests;

/// <summary>Construtores enxutos de IR para testar regras sem tocar em arquivos.</summary>
public static class RuleTestHarness
{
    private static SourceLocation Loc(string? symbol) => new("teste.msapp", "Controls/1.json", symbol, 0, 0);

    public static Control Ctl(string name, string template, params (string Property, string Script)[] properties)
    {
        var control = new Control { Name = name, TemplateName = template, Location = Loc(name) };
        foreach (var (property, script) in properties)
            control.Properties.Add(new PowerFxProperty(property, script, Loc($"{name}.{property}")));
        return control;
    }

    public static Control Screen(string name, params Control[] children)
    {
        var screen = new Control { Name = name, TemplateName = "screen", IsScreen = true, Location = Loc(name) };
        foreach (var child in children)
            screen.AddChild(child);
        return screen;
    }

    public static CanvasApp App(string name, params Control[] screens)
    {
        var app = new CanvasApp { Name = name, Location = Loc(null) };
        app.Screens.AddRange(screens);
        return app;
    }

    public static PowerPlatformProject ProjectWith(params CanvasApp[] apps)
    {
        var project = new PowerPlatformProject { SourcePath = "teste.msapp" };
        project.Apps.AddRange(apps);
        return project;
    }

    public static PowerPlatformProject ProjectWithFlows(params CloudFlow[] flows)
    {
        var project = new PowerPlatformProject { SourcePath = "teste.zip" };
        project.Flows.AddRange(flows);
        return project;
    }

    public static LintResult Run(IRule rule, PowerPlatformProject project, PpLintConfig? config = null) =>
        new RuleEngine([rule]).Run(project, config ?? PpLintConfig.Default);
}
```

- [ ] **Step 3: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/NamingRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Rules.Naming;
using static PpLint.Rules.Tests.RuleTestHarness;

namespace PpLint.Rules.Tests;

public class DefaultControlNameRuleTests
{
    [Fact]
    public void Reports_ControlNamedAfterItsTemplateWithNumber()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Button1", "button"))));
        var result = Run(new DefaultControlNameRule(), project);

        var d = Assert.Single(result.Diagnostics);
        Assert.Equal("NM010", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("Button1", d.Message);
    }

    [Fact]
    public void Reports_ScreenWithDefaultName()
    {
        var project = ProjectWith(App("A", Screen("Screen1")));
        Assert.Single(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void Reports_MultiDigitDefaultName()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Label12", "label"))));
        Assert.Single(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ProperlyNamedControl()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnSalvar", "button"))));
        Assert.Empty(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_NameThatMerelyContainsTemplateName()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnButton1Salvar", "button"))));
        Assert.Empty(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_NameEqualToTemplateWithoutNumber()
    {
        // "Button" sem dígito não é o nome que o Studio gera; não é violação desta regra.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Button", "button"))));
        Assert.Empty(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void Evaluates_EveryControlIncludingScreens()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnA", "button"), Ctl("btnB", "button"))));
        var tally = Assert.Single(Run(new DefaultControlNameRule(), project).Tallies);
        Assert.Equal(3, tally.Evaluated);
        Assert.Equal(0, tally.Violations);
    }

    [Fact]
    public void Evaluates_ZeroTargetsWhenThereAreNoApps()
    {
        var tally = Assert.Single(Run(new DefaultControlNameRule(), ProjectWith()).Tallies);
        Assert.Equal(0, tally.Evaluated);
    }
}

public class ControlPrefixRuleTests
{
    [Fact]
    public void Reports_ButtonWithoutBtnPrefix()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("SalvarPedido", "button"))));
        var d = Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics);

        Assert.Equal("NM011", d.RuleId);
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains("btn", d.Message);
        Assert.Contains("SalvarPedido", d.Message);
    }

    [Fact]
    public void DoesNotReport_ControlWithCorrectPrefix()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnSalvar", "button"), Ctl("lblTitulo", "label"))));
        Assert.Empty(Run(new ControlPrefixRule(), project).Diagnostics);
    }

    [Fact]
    public void DoesNotEvaluate_Screens()
    {
        var project = ProjectWith(App("A", Screen("QualquerNome", Ctl("btnOk", "button"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void DoesNotEvaluate_TemplateWithoutConfiguredPrefix()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("qualquerCoisa", "templateDesconhecido"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void DoesNotEvaluate_ControlWithDefaultName()
    {
        // Já reportado por NM010; contar aqui puniria o mesmo problema duas vezes no índice.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Button1", "button"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void UsesConfiguredPrefixInsteadOfPreset()
    {
        var config = PpLintConfig.Default with
        {
            Naming = new NamingConfig
            {
                ControlPrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["button"] = "bt",
                },
            },
        };
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btSalvar", "button"))));

        Assert.Empty(Run(new ControlPrefixRule(), project, config).Diagnostics);
    }

    [Fact]
    public void PrefixMatchIsCaseSensitiveOnTheName()
    {
        // "BtnSalvar" não atende ao prefixo "btn": a convenção é camelCase.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("BtnSalvar", "button"))));
        Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics);
    }

    [Fact]
    public void TemplateLookupIsCaseInsensitive()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnOk", "Button"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }
}
```

- [ ] **Step 4: Rodar o teste e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests`
Expected: FALHA de compilação — `DefaultControlNameRule` e `ControlPrefixRule` não existem.

- [ ] **Step 5: Implementar NM010**

`src/PpLint.Rules/Naming/DefaultControlNameRule.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Naming;

/// <summary>
/// NM010 — controle mantido com o nome que o Power Apps Studio gerou
/// (Button1, Label12, Screen1). Nome default torna toda fórmula que o
/// referencia ilegível e é o sintoma mais barato de detectar de um app
/// construído sem convenção.
/// </summary>
[Rule("NM010", RuleCategory.Naming, Severity.Error)]
public sealed class DefaultControlNameRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var control in app.AllControls())
            {
                ctx.Evaluated(1);

                if (HasDefaultName(control))
                {
                    ctx.Report(
                        control.Location,
                        $"O controle '{control.Name}' mantém o nome padrão gerado pelo Studio. "
                        + "Renomeie usando o prefixo do tipo do controle.");
                }
            }
        }
    }

    /// <summary>Nome default = nome do template seguido apenas de dígitos.</summary>
    internal static bool HasDefaultName(Control control)
    {
        var template = control.TemplateName;
        if (string.IsNullOrEmpty(template))
            return false;

        var name = control.Name;
        if (name.Length <= template.Length)
            return false;

        if (!name.StartsWith(template, StringComparison.OrdinalIgnoreCase))
            return false;

        for (var i = template.Length; i < name.Length; i++)
        {
            if (!char.IsAsciiDigit(name[i]))
                return false;
        }

        return true;
    }
}
```

- [ ] **Step 6: Implementar NM011**

`src/PpLint.Rules/Naming/ControlPrefixRule.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Naming;

/// <summary>
/// NM011 — o nome do controle deve começar com o prefixo do seu tipo
/// (btnSalvar, lblTitulo, galPedidos), de modo que qualquer fórmula revele
/// o tipo do que está sendo referenciado.
/// Telas ficam a cargo de NM012; controles com nome padrão, de NM010.
/// </summary>
[Rule("NM011", RuleCategory.Naming, Severity.Warning)]
public sealed class ControlPrefixRule : IRule
{
    public void Check(LintContext ctx)
    {
        var prefixes = ctx.Config.Naming.ControlPrefixes;

        foreach (var app in ctx.Project.Apps)
        {
            foreach (var control in app.AllControls())
            {
                if (control.IsScreen)
                    continue;

                if (!prefixes.TryGetValue(control.TemplateName, out var expected))
                    continue;

                if (DefaultControlNameRule.HasDefaultName(control))
                    continue;

                ctx.Evaluated(1);

                if (!control.Name.StartsWith(expected, StringComparison.Ordinal))
                {
                    ctx.Report(
                        control.Location,
                        $"O controle '{control.Name}' é do tipo '{control.TemplateName}' "
                        + $"e deveria começar com o prefixo '{expected}'.");
                }
            }
        }
    }
}
```

- [ ] **Step 7: Rodar o teste e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests`
Expected: PASSA — 16 testes (8 de NM010, 8 de NM011).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: regras NM010 e NM011 de nomenclatura de controles"
```

---

### Task 12: Grafo de variáveis e regra PF101

**Files:**
- Create: `src/PpLint.PowerFx/VariableGraph.cs`, `src/PpLint.Rules/Fx/UnusedGlobalVariableRule.cs`
- Test: `tests/PpLint.PowerFx.Tests/VariableGraphTests.cs`, `tests/PpLint.Rules.Tests/PowerFxRuleTests.cs`

**Interfaces:**
- Consumes: `PowerFxParser`, `AstWalker`, `CanvasApp`, `Control`, `PowerFxProperty`, `SourceLocation`, `IRule`, `LintContext`.
- Produces:
  - `sealed record GlobalVariableDefinition(string Name, SourceLocation Location)`.
  - `VariableGraph` com `IReadOnlyList<GlobalVariableDefinition> Globals`, `bool IsRead(string name)`, `static VariableGraph Build(CanvasApp app)`.
  - `UnusedGlobalVariableRule` (PF101, PowerFx, Warning).

**Semântica.** Uma variável global nasce de `Set(nome, valor)`. O primeiro argumento de `Set` é a **definição**, não uma leitura — todo outro identificador com aquele nome, em qualquer expressão do app, conta como leitura. Uma variável definida em dois lugares aparece uma única vez em `Globals` (a primeira definição encontrada), para respeitar o invariante de uma violação por alvo. Comparação de nomes segue o Power Fx: sem diferenciar maiúsculas.

- [ ] **Step 1: Escrever o teste do grafo**

`tests/PpLint.PowerFx.Tests/VariableGraphTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx.Tests;

public class VariableGraphTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static CanvasApp AppWith(params (string Property, string Script)[] properties)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var button = new Control { Name = "btnOk", TemplateName = "button", Location = Loc("btnOk") };
        foreach (var (property, script) in properties)
            button.Properties.Add(new PowerFxProperty(property, script, Loc($"btnOk.{property}")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);
        return app;
    }

    [Fact]
    public void Build_FindsGlobalDefinedBySet()
    {
        var graph = VariableGraph.Build(AppWith(("OnSelect", "Set(varTotal, 10)")));
        Assert.Equal(["varTotal"], graph.Globals.Select(g => g.Name));
    }

    [Fact]
    public void Build_SetTargetAloneIsNotARead()
    {
        var graph = VariableGraph.Build(AppWith(("OnSelect", "Set(varTotal, 10)")));
        Assert.False(graph.IsRead("varTotal"));
    }

    [Fact]
    public void Build_IdentifierElsewhereCountsAsRead()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varTotal, 10)"),
            ("Text", "varTotal")));
        Assert.True(graph.IsRead("varTotal"));
    }

    [Fact]
    public void Build_ReadInsideTheValueOfAnotherSetCounts()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varA, 1); Set(varB, varA + 1)")));
        Assert.True(graph.IsRead("varA"));
        Assert.False(graph.IsRead("varB"));
    }

    [Fact]
    public void Build_SelfReferenceCountsAsRead()
    {
        var graph = VariableGraph.Build(AppWith(("OnSelect", "Set(varContador, varContador + 1)")));
        Assert.True(graph.IsRead("varContador"));
    }

    [Fact]
    public void Build_NameComparisonIsCaseInsensitive()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varTotal, 1)"),
            ("Text", "VARTOTAL")));
        Assert.True(graph.IsRead("varTotal"));
    }

    [Fact]
    public void Build_DuplicateDefinitionsAppearOnce()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varTotal, 1)"),
            ("OnChange", "Set(varTotal, 2)")));
        Assert.Single(graph.Globals);
    }

    [Fact]
    public void Build_IncludesAppLevelProperties()
    {
        var app = AppWith(("Text", "varUsuario"));
        app.AppProperties.Add(new PowerFxProperty("OnStart", "Set(varUsuario, User().Email)", Loc("App.OnStart")));

        var graph = VariableGraph.Build(app);
        Assert.Equal(["varUsuario"], graph.Globals.Select(g => g.Name));
        Assert.True(graph.IsRead("varUsuario"));
    }

    [Fact]
    public void Build_UnparseableExpressionIsIgnored()
    {
        var graph = VariableGraph.Build(AppWith(
            ("OnSelect", "Set(varTotal, 1)"),
            ("Text", "Set(varOutra,")));
        Assert.Single(graph.Globals);
    }

    [Fact]
    public void Build_LocationPointsAtTheDefiningProperty()
    {
        var graph = VariableGraph.Build(AppWith(("OnSelect", "Set(varTotal, 10)")));
        Assert.Equal("btnOk.OnSelect", Assert.Single(graph.Globals).Location.Symbol);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter VariableGraphTests`
Expected: FALHA de compilação — `VariableGraph` não existe.

- [ ] **Step 3: Implementar o grafo**

`src/PpLint.PowerFx/VariableGraph.cs`:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.PowerFx;

public sealed record GlobalVariableDefinition(string Name, SourceLocation Location);

/// <summary>
/// Definições e leituras de variáveis globais de um canvas app.
/// O primeiro argumento de Set() é definição; qualquer outra ocorrência
/// do identificador, em qualquer expressão do app, é leitura.
/// </summary>
public sealed class VariableGraph
{
    private readonly Dictionary<string, GlobalVariableDefinition> _globals;
    private readonly HashSet<string> _reads;

    private VariableGraph(
        Dictionary<string, GlobalVariableDefinition> globals,
        HashSet<string> reads)
    {
        _globals = globals;
        _reads = reads;
    }

    public IReadOnlyList<GlobalVariableDefinition> Globals => _globals.Values.ToList();

    public bool IsRead(string name) => _reads.Contains(name);

    public static VariableGraph Build(CanvasApp app)
    {
        var globals = new Dictionary<string, GlobalVariableDefinition>(StringComparer.OrdinalIgnoreCase);
        var reads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in AllProperties(app))
        {
            var parsed = PowerFxParser.Parse(property.Script);
            if (parsed.Root is null)
                continue;

            var setTargets = new HashSet<TexlNode>();

            foreach (var call in AstWalker.Calls(parsed.Root, "Set"))
            {
                var target = FirstArgumentIdentifier(call);
                if (target is null)
                    continue;

                setTargets.Add(target);

                var name = target.Ident.Name.Value;
                if (!globals.ContainsKey(name))
                    globals[name] = new GlobalVariableDefinition(name, property.Location);
            }

            foreach (var identifier in AstWalker.Identifiers(parsed.Root))
            {
                if (!setTargets.Contains(identifier))
                    reads.Add(identifier.Ident.Name.Value);
            }
        }

        return new VariableGraph(globals, reads);
    }

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }

    /// <summary>O primeiro argumento da chamada, quando é um identificador simples.</summary>
    private static FirstNameNode? FirstArgumentIdentifier(CallNode call)
    {
        var args = call.Args?.Children;
        if (args is null || args.Count == 0)
            return null;

        return args[0] as FirstNameNode;
    }
}
```

> Se a versão instalada do pacote expuser os argumentos como `call.Args.ChildNodes` em vez de `call.Args.Children`, ajuste apenas essa linha.

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.PowerFx.Tests --filter VariableGraphTests`
Expected: PASSA — 10 testes.

- [ ] **Step 5: Escrever o teste da regra PF101**

`tests/PpLint.Rules.Tests/PowerFxRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Rules.Fx;
using static PpLint.Rules.Tests.RuleTestHarness;

namespace PpLint.Rules.Tests;

public class UnusedGlobalVariableRuleTests
{
    [Fact]
    public void Reports_GlobalThatIsNeverRead()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Set(varTotal, 10)")))));

        var d = Assert.Single(Run(new UnusedGlobalVariableRule(), project).Diagnostics);
        Assert.Equal("PF101", d.RuleId);
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains("varTotal", d.Message);
    }

    [Fact]
    public void DoesNotReport_GlobalReadInAnotherControl()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Set(varTotal, 10)")),
            Ctl("lblTotal", "label", ("Text", "varTotal")))));

        Assert.Empty(Run(new UnusedGlobalVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void Evaluates_OneTargetPerGlobalVariable()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Set(varA, 1); Set(varB, 2)")),
            Ctl("lblA", "label", ("Text", "varA")))));

        var result = Run(new UnusedGlobalVariableRule(), project);
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void Evaluates_ZeroWhenAppHasNoGlobals()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Notify(\"oi\")")))));

        Assert.Equal(0, Assert.Single(Run(new UnusedGlobalVariableRule(), project).Tallies).Evaluated);
    }

    [Fact]
    public void ReportsOncePerVariableEvenWithTwoDefinitions()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnOk", "button", ("OnSelect", "Set(varTotal, 1)"), ("OnChange", "Set(varTotal, 2)")))));

        Assert.Single(Run(new UnusedGlobalVariableRule(), project).Diagnostics);
    }

    [Fact]
    public void HandlesMultipleAppsIndependently()
    {
        var appA = App("A", Screen("scrA", Ctl("btnA", "button", ("OnSelect", "Set(varX, 1)"))));
        var appB = App("B", Screen("scrB",
            Ctl("btnB", "button", ("OnSelect", "Set(varX, 1)")),
            Ctl("lblB", "label", ("Text", "varX"))));

        // varX é lida no app B, mas não no app A: uma violação apenas.
        var result = Run(new UnusedGlobalVariableRule(), ProjectWith(appA, appB));
        Assert.Single(result.Diagnostics);
        Assert.Equal(2, Assert.Single(result.Tallies).Evaluated);
    }
}
```

- [ ] **Step 6: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UnusedGlobalVariableRuleTests`
Expected: FALHA de compilação — `UnusedGlobalVariableRule` não existe.

- [ ] **Step 7: Implementar PF101**

`src/PpLint.Rules/Fx/UnusedGlobalVariableRule.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF101 — variável global definida por Set() e nunca lida em nenhuma
/// expressão do app. Costuma ser resquício de refatoração e mantém em
/// memória estado que ninguém consome.
/// </summary>
[Rule("PF101", RuleCategory.PowerFx, Severity.Warning)]
public sealed class UnusedGlobalVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Globals)
            {
                ctx.Evaluated(1);

                if (!graph.IsRead(variable.Name))
                {
                    ctx.Report(
                        variable.Location,
                        $"A variável global '{variable.Name}' é definida e nunca lida. "
                        + "Remova o Set() ou passe a usar o valor.");
                }
            }
        }
    }
}
```

- [ ] **Step 8: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UnusedGlobalVariableRuleTests`
Expected: PASSA — 6 testes.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: grafo de variáveis e regra PF101"
```

---

### Task 13: Regra PF110 (condição constante)

**Files:**
- Create: `src/PpLint.Rules/Fx/ConstantConditionRule.cs`
- Modify: `tests/PpLint.Rules.Tests/PowerFxRuleTests.cs` (acrescentar a classe de teste ao fim do arquivo)

**Interfaces:**
- Consumes: `PowerFxParser`, `AstWalker`, `IRule`, `LintContext`, `CanvasApp`.
- Produces: `ConstantConditionRule` (PF110, PowerFx, Error).

**Semântica.** Uma condição é constante quando seu valor não depende de nada em tempo de execução:
1. Comparação entre dois literais numéricos: `2 > 1`, `1 = 1`.
2. Literal booleano usado como condição de `If`: `If(true, …)`.

O alvo avaliado é **cada condição examinada**, não cada expressão — assim uma propriedade com três `If` contribui com três alvos. Somente o primeiro argumento de `If` é tratado como condição; comparações de literais são detectadas em qualquer posição, porque `2 > 1` nunca é intencional.

- [ ] **Step 1: Escrever o teste que falha**

Acrescentar ao fim de `tests/PpLint.Rules.Tests/PowerFxRuleTests.cs`:

```csharp
public class ConstantConditionRuleTests
{
    private static PpLint.Core.Rules.LintResult Check(string script) =>
        Run(new ConstantConditionRule(),
            ProjectWith(App("A", Screen("scrHome", Ctl("btnOk", "button", ("OnSelect", script))))));

    [Fact]
    public void Reports_ComparisonBetweenNumericLiterals()
    {
        var d = Assert.Single(Check("If(2 > 1, Notify(\"sempre\"))").Diagnostics);
        Assert.Equal("PF110", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void Reports_EqualityBetweenNumericLiterals()
    {
        Assert.Single(Check("If(1 = 1, Notify(\"sempre\"))").Diagnostics);
    }

    [Fact]
    public void Reports_BooleanLiteralAsIfCondition()
    {
        Assert.Single(Check("If(true, Notify(\"sempre\"))").Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ConditionUsingAVariable()
    {
        Assert.Empty(Check("If(varTotal > 1, Notify(\"talvez\"))").Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ConditionUsingAFunctionCall()
    {
        Assert.Empty(Check("If(IsBlank(txtNome.Text), Notify(\"vazio\"))").Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ArithmeticBetweenLiterals()
    {
        // 1 + 2 é cálculo, não condição; só comparações constantes interessam.
        Assert.Empty(Check("Set(varTotal, 1 + 2)").Diagnostics);
    }

    [Fact]
    public void DoesNotReport_BooleanLiteralOutsideACondition()
    {
        Assert.Empty(Check("Set(varAtivo, true)").Diagnostics);
    }

    [Fact]
    public void Evaluates_EachConditionSeparately()
    {
        var result = Check("If(varA, 1, If(2 > 1, 2, 3))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void Evaluates_ZeroWhenThereAreNoConditions()
    {
        Assert.Equal(0, Assert.Single(Check("Notify(\"oi\")").Tallies).Evaluated);
    }

    [Fact]
    public void UnparseableExpressionIsIgnored()
    {
        Assert.Empty(Check("If(2 > 1,").Diagnostics);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter ConstantConditionRuleTests`
Expected: FALHA de compilação — `ConstantConditionRule` não existe.

- [ ] **Step 3: Implementar PF110**

`src/PpLint.Rules/Fx/ConstantConditionRule.cs`:

```csharp
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF110 — condição cujo resultado não depende de nada em tempo de execução,
/// como If(2 &gt; 1, ...) ou If(true, ...). Ou é resto de depuração, ou é um
/// ramo que nunca executa; nos dois casos o comportamento real diverge do
/// que o código aparenta.
/// </summary>
[Rule("PF110", RuleCategory.PowerFx, Severity.Error)]
public sealed class ConstantConditionRule : IRule
{
    private static readonly BinaryOp[] ComparisonOperators =
    [
        BinaryOp.Equal,
        BinaryOp.NotEqual,
        BinaryOp.Less,
        BinaryOp.LessEqual,
        BinaryOp.Greater,
        BinaryOp.GreaterEqual,
    ];

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var property in AllProperties(app))
            {
                var parsed = PowerFxParser.Parse(property.Script);
                if (parsed.Root is null)
                    continue;

                foreach (var condition in Conditions(parsed.Root))
                {
                    ctx.Evaluated(1);

                    if (IsConstant(condition))
                    {
                        ctx.Report(
                            property.Location,
                            $"A condição '{condition}' tem resultado constante e não depende de nada "
                            + "em tempo de execução. Remova a condição ou use um valor real.");
                    }
                }
            }
        }
    }

    /// <summary>Condições examinadas: o primeiro argumento de cada If e toda comparação.</summary>
    private static IEnumerable<TexlNode> Conditions(TexlNode root)
    {
        var seen = new HashSet<TexlNode>();

        foreach (var call in AstWalker.Calls(root, "If"))
        {
            var args = call.Args?.Children;
            if (args is { Count: > 0 } && seen.Add(args[0]))
                yield return args[0];
        }

        foreach (var node in AstWalker.Descendants(root).OfType<BinaryOpNode>())
        {
            if (ComparisonOperators.Contains(node.Op) && seen.Add(node))
                yield return node;
        }
    }

    private static bool IsConstant(TexlNode condition) => condition switch
    {
        BoolLitNode => true,
        BinaryOpNode binary => ComparisonOperators.Contains(binary.Op)
                               && IsLiteral(binary.Left)
                               && IsLiteral(binary.Right),
        _ => false,
    };

    private static bool IsLiteral(TexlNode node) =>
        node is NumLitNode or BoolLitNode or StrLitNode;

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

> Se a versão instalada usar `DecLitNode` para literais decimais, acrescente-o a `IsLiteral`. Se `BinaryOp` tiver outro nome de membro (por exemplo `BinaryOp.GreaterThan`), ajuste a lista `ComparisonOperators` — o spike da Task 8 já revelou os nomes disponíveis.

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter ConstantConditionRuleTests`
Expected: PASSA — 10 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: regra PF110 de condição constante"
```

---

### Task 14: Regra FL201 (variável de fluxo não usada)

**Files:**
- Create: `src/PpLint.Rules/Flow/UnusedFlowVariableRule.cs`
- Test: `tests/PpLint.Rules.Tests/FlowRuleTests.cs`

**Interfaces:**
- Consumes: `CloudFlow`, `FlowAction`, `FlowVariable`, `IRule`, `LintContext`, `RuleTestHarness.ProjectWithFlows`.
- Produces: `UnusedFlowVariableRule` (FL201, Flow, Warning).

**Semântica.** Uma variável de fluxo é lida quando alguma expressão do fluxo contém `variables('nome')`. `SetVariable`, `IncrementVariable` e `AppendToArrayVariable` são **escritas**, não leituras: uma variável apenas escrita continua sendo não usada. A busca é textual sobre as expressões coletadas pelo `FlowExtractor`, sem diferenciar maiúsculas, tolerando espaços dentro dos parênteses.

- [ ] **Step 1: Escrever o teste que falha**

`tests/PpLint.Rules.Tests/FlowRuleTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Rules.Flow;
using static PpLint.Rules.Tests.RuleTestHarness;

namespace PpLint.Rules.Tests;

public class UnusedFlowVariableRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static CloudFlow FlowWith(
        (string Name, string Type)[] variables,
        params (string ActionName, string[] Expressions)[] actions)
    {
        var flow = new CloudFlow { Name = "AprovarPedido", Location = Loc("AprovarPedido") };

        foreach (var (name, type) in variables)
            flow.Variables.Add(new FlowVariable(name, type, Loc("Inicializar")));

        foreach (var (actionName, expressions) in actions)
        {
            var action = new FlowAction { Name = actionName, Type = "Compose", Location = Loc(actionName) };
            action.Expressions.AddRange(expressions);
            flow.Actions.Add(action);
        }

        return flow;
    }

    [Fact]
    public void Reports_VariableThatIsNeverRead()
    {
        var flow = FlowWith([("varContador", "integer")], ("Compor", ["texto qualquer"]));
        var d = Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);

        Assert.Equal("FL201", d.RuleId);
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains("varContador", d.Message);
    }

    [Fact]
    public void DoesNotReport_VariableReadInAnExpression()
    {
        var flow = FlowWith([("varContador", "integer")], ("Compor", ["@{variables('varContador')}"]));
        Assert.Empty(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ReadWithSpacesInsideParentheses()
    {
        var flow = FlowWith([("varContador", "integer")], ("Compor", ["@{variables( 'varContador' )}"]));
        Assert.Empty(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void ReadDetectionIsCaseInsensitive()
    {
        var flow = FlowWith([("varContador", "integer")], ("Compor", ["@{VARIABLES('VARCONTADOR')}"]));
        Assert.Empty(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void Reports_VariableOnlyWritten()
    {
        // O nome aparece nos inputs do SetVariable, mas escrita não é leitura.
        var flow = FlowWith([("varContador", "integer")], ("Definir", ["varContador", "10"]));
        Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_VariableReadInsideNestedAction()
    {
        var flow = FlowWith([("varContador", "integer")]);
        var loop = new FlowAction { Name = "Apply_to_each", Type = "Foreach", Location = Loc("Apply_to_each") };
        var inner = new FlowAction { Name = "Compor", Type = "Compose", Location = Loc("Compor") };
        inner.Expressions.Add("@{variables('varContador')}");
        loop.Children.Add(inner);
        flow.Actions.Add(loop);

        Assert.Empty(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void Evaluates_OneTargetPerVariable()
    {
        var flow = FlowWith(
            [("varA", "integer"), ("varB", "string")],
            ("Compor", ["@{variables('varA')}"]));

        var tally = Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Tallies);
        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void Evaluates_ZeroWhenFlowHasNoVariables()
    {
        var flow = FlowWith([], ("Compor", ["x"]));
        Assert.Equal(0, Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Tallies).Evaluated);
    }

    [Fact]
    public void DoesNotMatchVariableWithSimilarName()
    {
        var flow = FlowWith([("varConta", "integer")], ("Compor", ["@{variables('varContador')}"]));
        Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void HandlesMultipleFlowsIndependently()
    {
        var usada = FlowWith([("varA", "integer")], ("Compor", ["@{variables('varA')}"]));
        var naoUsada = FlowWith([("varB", "integer")], ("Compor", ["nada"]));

        var result = Run(new UnusedFlowVariableRule(), ProjectWithFlows(usada, naoUsada));
        Assert.Single(result.Diagnostics);
        Assert.Equal(2, Assert.Single(result.Tallies).Evaluated);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UnusedFlowVariableRuleTests`
Expected: FALHA de compilação — `UnusedFlowVariableRule` não existe.

- [ ] **Step 3: Implementar FL201**

`src/PpLint.Rules/Flow/UnusedFlowVariableRule.cs`:

```csharp
using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL201 — variável inicializada no fluxo e nunca lida. Cada
/// InitializeVariable custa uma ação no limite do fluxo e sugere um estado
/// que ninguém consome. Escrever (SetVariable) não conta como uso.
/// </summary>
[Rule("FL201", RuleCategory.Flow, Severity.Warning)]
public sealed class UnusedFlowVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            var expressions = flow.AllActions().SelectMany(a => a.Expressions).ToList();

            foreach (var variable in flow.Variables)
            {
                ctx.Evaluated(1);

                if (!IsRead(variable.Name, expressions))
                {
                    ctx.Report(
                        variable.Location,
                        $"A variável '{variable.Name}' é inicializada no fluxo '{flow.Name}' e nunca lida. "
                        + "Remova a inicialização ou passe a usar o valor.");
                }
            }
        }
    }

    private static bool IsRead(string name, IReadOnlyList<string> expressions)
    {
        var pattern = $@"variables\(\s*'{Regex.Escape(name)}'\s*\)";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return expressions.Any(e => regex.IsMatch(e));
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter UnusedFlowVariableRuleTests`
Expected: PASSA — 10 testes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: regra FL201 de variável de fluxo não usada"
```

---

### Task 15: Relatório de terminal e wiring do comando check

**Files:**
- Create: `src/PpLint.Cli/AnsiColors.cs`, `src/PpLint.Cli/TextReporter.cs`
- Modify: `src/PpLint.Cli/Program.cs` (substituir o conteúdo gerado pelo template)
- Test: `tests/PpLint.Cli.Tests/TextReporterTests.cs`

**Interfaces:**
- Consumes: `LintResult`, `Diagnostic`, `ComplianceReport`, `ComplianceScorer`, `Severity`, `CliOptions`, `ProjectLoader`, `RuleEngine`, `PpLintConfig`.
- Produces: `static string TextReporter.Render(IReadOnlyList<Diagnostic> diagnostics, ComplianceReport compliance, TimeSpan elapsed, bool useColor, bool quiet)`; `static int Program.Run(string[] args, TextWriter stdout, TextWriter stderr)` — testável, sem depender de `Console`.

**Formato.** Diagnósticos agrupados por artefato e, dentro dele, por símbolo, na ordem que o motor já devolveu (severidade decrescente). Depois o bloco de conformidade — global e por categoria com as contagens absolutas — e o resumo com contagens por severidade, tempo e os três IDs mais frequentes. Com `--quiet`, só o bloco de conformidade e o resumo.

- [ ] **Step 1: Adicionar as referências que faltam**

```bash
cd /c/PROJETOS/pp-lint
dotnet add src/PpLint.Cli reference src/PpLint.Extractors
dotnet add src/PpLint.Cli reference src/PpLint.Rules
```

- [ ] **Step 2: Escrever o teste que falha**

`tests/PpLint.Cli.Tests/TextReporterTests.cs`:

```csharp
using PpLint.Core;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;

namespace PpLint.Cli.Tests;

public class TextReporterTests
{
    private static Diagnostic Diag(string ruleId, Severity severity, string symbol, string message) =>
        new(ruleId, RuleCategory.Naming, severity, message,
            new SourceLocation("MinhaSolucao.zip", "CanvasApps/App.msapp", symbol, 0, 0));

    private static ComplianceReport Compliance() =>
        ComplianceScorer.Compute([
            new RuleTally("NM010", RuleCategory.Naming, Severity.Error, 100, 2),
            new RuleTally("PF101", RuleCategory.PowerFx, Severity.Warning, 50, 0),
        ]);

    private static string Render(params Diagnostic[] diagnostics) =>
        TextReporter.Render(diagnostics, Compliance(), TimeSpan.FromSeconds(1.2), useColor: false, quiet: false);

    [Fact]
    public void Render_ShowsRuleIdSeverityAndMessage()
    {
        var output = Render(Diag("NM010", Severity.Error, "Screen1", "Controle com nome padrão"));

        Assert.Contains("NM010", output);
        Assert.Contains("error", output);
        Assert.Contains("Controle com nome padrão", output);
        Assert.Contains("Screen1", output);
    }

    [Fact]
    public void Render_GroupsByArtifactPath()
    {
        var output = Render(
            Diag("NM010", Severity.Error, "Screen1", "a"),
            Diag("NM011", Severity.Warning, "Botao", "b"));

        Assert.Equal(1, CountOccurrences(output, "MinhaSolucao.zip"));
    }

    [Fact]
    public void Render_ShowsOverallCompliance()
    {
        var output = Render(Diag("NM010", Severity.Error, "Screen1", "a"));
        Assert.Contains("Conformidade geral", output);
    }

    [Fact]
    public void Render_ShowsCategoryBreakdownWithCounts()
    {
        var output = Render(Diag("NM010", Severity.Error, "Screen1", "a"));

        Assert.Contains("Nomenclatura", output);
        Assert.Contains("98/100", output);
        Assert.Contains("Power Fx", output);
    }

    [Fact]
    public void Render_ShowsSummaryCounts()
    {
        var output = Render(
            Diag("NM010", Severity.Error, "A", "a"),
            Diag("NM011", Severity.Warning, "B", "b"),
            Diag("NM011", Severity.Warning, "C", "c"));

        Assert.Contains("1 erro", output);
        Assert.Contains("2 avisos", output);
    }

    [Fact]
    public void Render_ShowsTopOffenders()
    {
        var output = Render(
            Diag("NM011", Severity.Warning, "A", "a"),
            Diag("NM011", Severity.Warning, "B", "b"),
            Diag("NM010", Severity.Error, "C", "c"));

        Assert.Contains("NM011 (2", output);
    }

    [Fact]
    public void Render_QuietOmitsIndividualDiagnostics()
    {
        var output = TextReporter.Render(
            [Diag("NM010", Severity.Error, "Screen1", "mensagem detalhada")],
            Compliance(), TimeSpan.FromSeconds(1), useColor: false, quiet: true);

        Assert.DoesNotContain("mensagem detalhada", output);
        Assert.Contains("Conformidade geral", output);
    }

    [Fact]
    public void Render_NoDiagnosticsShowsCleanMessage()
    {
        var output = TextReporter.Render([], Compliance(), TimeSpan.FromSeconds(1), useColor: false, quiet: false);
        Assert.Contains("Nenhum achado", output);
    }

    [Fact]
    public void Render_WithColorEmitsAnsiCodes()
    {
        var output = TextReporter.Render(
            [Diag("NM010", Severity.Error, "A", "a")],
            Compliance(), TimeSpan.FromSeconds(1), useColor: true, quiet: false);

        Assert.Contains("\u001b[", output);
    }

    [Fact]
    public void Render_WithoutColorEmitsNoAnsiCodes()
    {
        Assert.DoesNotContain("\u001b[", Render(Diag("NM010", Severity.Error, "A", "a")));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
```

- [ ] **Step 3: Rodar e confirmar que falha**

Run: `dotnet test tests/PpLint.Cli.Tests --filter TextReporterTests`
Expected: FALHA de compilação — `TextReporter` não existe.

- [ ] **Step 4: Implementar as cores**

`src/PpLint.Cli/AnsiColors.cs`:

```csharp
namespace PpLint.Cli;

/// <summary>Códigos ANSI mínimos — sem dependência externa, seguros para AOT.</summary>
internal static class AnsiColors
{
    public const string Reset = "\u001b[0m";
    public const string Bold = "\u001b[1m";
    public const string Dim = "\u001b[2m";
    public const string Red = "\u001b[31m";
    public const string Yellow = "\u001b[33m";
    public const string Blue = "\u001b[34m";
    public const string Green = "\u001b[32m";

    public static string Apply(string text, string color, bool enabled) =>
        enabled ? color + text + Reset : text;
}
```

- [ ] **Step 5: Implementar o relatório**

`src/PpLint.Cli/TextReporter.cs`:

```csharp
using System.Globalization;
using System.Text;
using PpLint.Core;
using PpLint.Core.Scoring;

namespace PpLint.Cli;

public static class TextReporter
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static string Render(
        IReadOnlyList<Diagnostic> diagnostics,
        ComplianceReport compliance,
        TimeSpan elapsed,
        bool useColor,
        bool quiet)
    {
        var sb = new StringBuilder();

        if (!quiet)
            RenderDiagnostics(sb, diagnostics, useColor);

        RenderCompliance(sb, compliance, useColor);
        RenderSummary(sb, diagnostics, elapsed, useColor);

        return sb.ToString();
    }

    private static void RenderDiagnostics(StringBuilder sb, IReadOnlyList<Diagnostic> diagnostics, bool color)
    {
        if (diagnostics.Count == 0)
        {
            sb.AppendLine(AnsiColors.Apply("Nenhum achado.", AnsiColors.Green, color));
            sb.AppendLine();
            return;
        }

        foreach (var byArtifact in diagnostics.GroupBy(d => d.Location.ArtifactPath))
        {
            sb.AppendLine(AnsiColors.Apply(byArtifact.Key, AnsiColors.Bold, color));

            foreach (var byEntry in byArtifact.GroupBy(d => d.Location.EntryPath))
            {
                if (!string.IsNullOrEmpty(byEntry.Key))
                    sb.Append("  ").AppendLine(AnsiColors.Apply(byEntry.Key, AnsiColors.Dim, color));

                foreach (var bySymbol in byEntry.GroupBy(d => d.Location.Symbol ?? string.Empty))
                {
                    if (!string.IsNullOrEmpty(bySymbol.Key))
                        sb.Append("    ").AppendLine(bySymbol.Key);

                    foreach (var d in bySymbol)
                    {
                        sb.Append("      ")
                          .Append(AnsiColors.Apply(Label(d.Severity).PadRight(7), ColorOf(d.Severity), color))
                          .Append(' ')
                          .Append(d.RuleId)
                          .Append("  ")
                          .AppendLine(d.Message);
                    }
                }
            }

            sb.AppendLine();
        }
    }

    private static void RenderCompliance(StringBuilder sb, ComplianceReport compliance, bool color)
    {
        var overall = Percent(compliance.Overall.Percent);
        sb.Append("  ")
          .Append(AnsiColors.Apply("Conformidade geral: ", AnsiColors.Bold, color))
          .AppendLine(AnsiColors.Apply(overall, ColorForScore(compliance.Overall.Percent), color));

        foreach (var (category, score) in compliance.ByCategory.OrderBy(p => CategoryName(p.Key), StringComparer.Ordinal))
        {
            var conformes = score.EvaluatedTargets - score.Violations;
            sb.Append("    ")
              .Append(CategoryName(category).PadRight(14))
              .Append(Percent(score.Percent).PadLeft(7))
              .Append("  (")
              .Append(conformes.ToString(Br))
              .Append('/')
              .Append(score.EvaluatedTargets.ToString(Br))
              .AppendLine(" conformes)");
        }

        sb.AppendLine();
    }

    private static void RenderSummary(
        StringBuilder sb, IReadOnlyList<Diagnostic> diagnostics, TimeSpan elapsed, bool color)
    {
        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = diagnostics.Count(d => d.Severity == Severity.Warning);
        var infos = diagnostics.Count(d => d.Severity == Severity.Info);

        sb.Append("  Resumo: ")
          .Append(Plural(errors, "erro", "erros")).Append(", ")
          .Append(Plural(warnings, "aviso", "avisos")).Append(", ")
          .Append(Plural(infos, "informação", "informações"))
          .Append(" em ")
          .Append(elapsed.TotalSeconds.ToString("0.0", Br))
          .AppendLine(" s");

        var top = diagnostics
            .GroupBy(d => d.RuleId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();

        if (top.Count > 0)
            sb.Append("  Principais ocorrências: ").AppendLine(string.Join(", ", top));

        _ = color;
    }

    private static string Percent(double value) => value.ToString("0.0", Br) + "%";

    private static string Plural(int count, string singular, string plural) =>
        $"{count.ToString(Br)} {(count == 1 ? singular : plural)}";

    private static string Label(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "info",
    };

    private static string ColorOf(Severity severity) => severity switch
    {
        Severity.Error => AnsiColors.Red,
        Severity.Warning => AnsiColors.Yellow,
        _ => AnsiColors.Blue,
    };

    private static string ColorForScore(double percent) => percent switch
    {
        >= 95 => AnsiColors.Green,
        >= 80 => AnsiColors.Yellow,
        _ => AnsiColors.Red,
    };

    private static string CategoryName(RuleCategory category) => category switch
    {
        RuleCategory.Naming => "Nomenclatura",
        RuleCategory.PowerFx => "Power Fx",
        RuleCategory.Flow => "Fluxos",
        RuleCategory.Duplication => "Duplicação",
        RuleCategory.Performance => "Performance",
        RuleCategory.Security => "Segurança",
        RuleCategory.Solution => "Solução",
        _ => category.ToString(),
    };
}
```

- [ ] **Step 6: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Cli.Tests --filter TextReporterTests`
Expected: PASSA — 10 testes.

- [ ] **Step 7: Implementar o Program**

Substituir todo o conteúdo de `src/PpLint.Cli/Program.cs`:

```csharp
using System.Diagnostics;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;
using PpLint.Extractors;
using PpLint.Rules.Naming;

namespace PpLint.Cli;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var parsed = ArgumentParser.Parse(args);
        if (!parsed.IsSuccess)
        {
            stderr.WriteLine(parsed.Error);
            return 2;
        }

        var options = parsed.Value!;

        switch (options.Command)
        {
            case CliCommand.Help:
                stdout.WriteLine(HelpText);
                return 0;

            case CliCommand.Version:
                stdout.WriteLine($"pp-lint {typeof(Program).Assembly.GetName().Version}");
                return 0;

            case CliCommand.Rules:
                foreach (var id in KnownRuleIds())
                    stdout.WriteLine(id);
                return 0;

            case CliCommand.Explain:
                stdout.WriteLine($"Documentação da regra {options.ExplainRuleId} disponível a partir da Fase 2.");
                return 0;

            case CliCommand.Check:
                return RunCheck(options, stdout, stderr);

            default:
                stderr.WriteLine("Comando não implementado.");
                return 2;
        }
    }

    private static int RunCheck(CliOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (options.Format != "text")
        {
            stderr.WriteLine($"O formato '{options.Format}' será entregue na Fase 2. Use 'text'.");
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

            var result = engine.Run(project, PpLintConfig.Default);
            diagnostics.AddRange(result.Diagnostics);
            tallies.AddRange(result.Tallies);
        }

        stopwatch.Stop();

        var compliance = ComplianceScorer.Compute(tallies);
        var useColor = !options.NoColor && !Console.IsOutputRedirected;

        stdout.Write(TextReporter.Render(diagnostics, compliance, stopwatch.Elapsed, useColor, options.Quiet));

        return diagnostics.Any(d => d.Severity >= options.FailOn) ? 1 : 0;
    }

    private static IEnumerable<string> KnownRuleIds() =>
        typeof(DefaultControlNameRule).Assembly
            .GetTypes()
            .Select(t => t.GetCustomAttributes(typeof(RuleAttribute), false).FirstOrDefault())
            .OfType<RuleAttribute>()
            .Select(a => $"{a.Id}  {a.Category}  {a.DefaultSeverity}")
            .OrderBy(s => s, StringComparer.Ordinal);

    private const string HelpText = """
        pp-lint — linter para artefatos do Power Platform

        Uso:
          pp-lint check <caminho...> [opções]   analisa .zip, .msapp ou pasta
          pp-lint rules                         lista as regras disponíveis
          pp-lint explain <ID>                  documentação de uma regra
          pp-lint --version                     mostra a versão

        Opções:
          --format <text|json|sarif|html|md>    formato de saída (padrão: text)
          --output <arquivo>                    grava a saída em arquivo
          --fail-on <error|warning|info>        severidade que retorna código 1 (padrão: error)
          --no-color                            desativa cores
          --quiet                               omite os achados individuais

        Códigos de saída:
          0  nenhum achado no nível de --fail-on
          1  achados no nível de --fail-on ou acima
          2  erro de execução
        """;
}
```

- [ ] **Step 8: Rodar toda a suíte e o binário**

```bash
cd /c/PROJETOS/pp-lint
dotnet test
dotnet run --project src/PpLint.Cli -- --help
```

Expected: todos os testes passam; a ajuda é impressa e o processo sai com código 0.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: relatório de terminal e comando check"
```

---

### Task 16: Robustez, teste ponta a ponta e contrato do catálogo

**Files:**
- Test: `tests/PpLint.Cli.Tests/EndToEndTests.cs`, `tests/PpLint.Rules.Tests/RuleCatalogContractTests.cs`

**Interfaces:**
- Consumes: `Program.Run`, `RuleEngine.CreateDefault`, `RuleAttribute`, `TestZip` (replicado localmente, pois é interno ao projeto de testes de extractors).
- Produces: nenhuma API nova — esta task fecha o contrato de qualidade da fase.

**Contrato do catálogo.** O build deve falhar se alguém registrar uma regra sem `[Rule]`, com ID duplicado, com ID fora do padrão de prefixo, ou que não chame `Evaluated`. É a salvaguarda que impede o índice de conformidade de se degradar silenciosamente conforme o catálogo cresce nas fases seguintes.

- [ ] **Step 1: Escrever o teste de contrato do catálogo**

`tests/PpLint.Rules.Tests/RuleCatalogContractTests.cs`:

```csharp
using System.Reflection;
using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Naming;
using static PpLint.Rules.Tests.RuleTestHarness;

namespace PpLint.Rules.Tests;

public class RuleCatalogContractTests
{
    private static readonly Assembly RulesAssembly = typeof(DefaultControlNameRule).Assembly;

    private static IEnumerable<(Type Type, RuleAttribute Meta)> Catalog() =>
        RulesAssembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IRule).IsAssignableFrom(t))
            .Select(t => (Type: t, Meta: t.GetCustomAttribute<RuleAttribute>()!))
            .Where(x => x.Meta is not null);

    [Fact]
    public void EveryRuleImplementationDeclaresTheRuleAttribute()
    {
        var missing = RulesAssembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IRule).IsAssignableFrom(t)
                        && t.GetCustomAttribute<RuleAttribute>() is null)
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void RuleIdsAreUnique()
    {
        var duplicated = Catalog()
            .GroupBy(x => x.Meta.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicated);
    }

    [Fact]
    public void RuleIdsFollowThePrefixConvention()
    {
        var pattern = new Regex("^(NM|PF|FL|DUP|PERF|SEC|SOL)[0-9]{3}$");
        var invalid = Catalog().Where(x => !pattern.IsMatch(x.Meta.Id)).Select(x => x.Meta.Id).ToList();

        Assert.Empty(invalid);
    }

    [Fact]
    public void RuleIdPrefixMatchesItsCategory()
    {
        var expected = new Dictionary<string, RuleCategory>
        {
            ["NM"] = RuleCategory.Naming,
            ["PF"] = RuleCategory.PowerFx,
            ["FL"] = RuleCategory.Flow,
            ["DUP"] = RuleCategory.Duplication,
            ["PERF"] = RuleCategory.Performance,
            ["SEC"] = RuleCategory.Security,
            ["SOL"] = RuleCategory.Solution,
        };

        foreach (var (type, meta) in Catalog())
        {
            var prefix = expected.Keys.First(p => meta.Id.StartsWith(p, StringComparison.Ordinal));
            Assert.True(
                expected[prefix] == meta.Category,
                $"{type.Name}: o ID {meta.Id} não corresponde à categoria {meta.Category}.");
        }
    }

    [Fact]
    public void EveryRuleHasAParameterlessConstructor()
    {
        var invalid = Catalog()
            .Where(x => x.Type.GetConstructor(Type.EmptyTypes) is null)
            .Select(x => x.Type.FullName)
            .ToList();

        Assert.Empty(invalid);
    }

    [Fact]
    public void EveryRuleCountsEvaluatedTargetsOnARepresentativeProject()
    {
        // Projeto com um app e um fluxo, ambos com conteúdo: nenhuma regra
        // desta fase pode terminar sem ter examinado alvo algum.
        var app = App("A", Screen("scrHome",
            Ctl("btnSalvar", "button", ("OnSelect", "Set(varTotal, 1); If(varTotal > 0, Notify(\"ok\"))"))));

        var flow = new CloudFlow
        {
            Name = "F",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "F", 0, 0),
        };
        flow.Variables.Add(new FlowVariable("varX", "integer",
            new SourceLocation("teste.zip", "Workflows/f.json", "Inicializar", 0, 0)));

        var project = ProjectWith(app);
        project.Flows.Add(flow);

        var result = RuleEngine.CreateDefault(RulesAssembly).Run(project, PpLintConfig.Default);

        var silent = result.Tallies.Where(t => t.Evaluated == 0).Select(t => t.RuleId).ToList();
        Assert.Empty(silent);
    }

    [Fact]
    public void NoRuleReportsMoreViolationsThanTargetsEvaluated()
    {
        var app = App("A", Screen("Screen1",
            Ctl("Button1", "button", ("OnSelect", "Set(varNaoUsada, 1); If(2 > 1, Notify(\"x\"))")),
            Ctl("SemPrefixo", "label", ("Text", "\"a\""))));

        var flow = new CloudFlow
        {
            Name = "F",
            Location = new SourceLocation("teste.zip", "Workflows/f.json", "F", 0, 0),
        };
        flow.Variables.Add(new FlowVariable("varY", "integer",
            new SourceLocation("teste.zip", "Workflows/f.json", "Inicializar", 0, 0)));

        var project = ProjectWith(app);
        project.Flows.Add(flow);

        var result = RuleEngine.CreateDefault(RulesAssembly).Run(project, PpLintConfig.Default);

        foreach (var tally in result.Tallies)
        {
            Assert.True(
                tally.Violations <= tally.Evaluated,
                $"{tally.RuleId} violou o invariante do índice: {tally.Violations} > {tally.Evaluated}.");
        }
    }

    [Fact]
    public void CatalogContainsThePhaseOneRules()
    {
        var ids = Catalog().Select(x => x.Meta.Id).ToList();
        Assert.Contains("NM010", ids);
        Assert.Contains("NM011", ids);
        Assert.Contains("PF101", ids);
        Assert.Contains("PF110", ids);
        Assert.Contains("FL201", ids);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Rules.Tests --filter RuleCatalogContractTests`
Expected: PASSA — 8 testes. Se `EveryRuleCountsEvaluatedTargetsOnARepresentativeProject` falhar, a regra apontada esqueceu de chamar `ctx.Evaluated`; corrija a regra, não o teste.

- [ ] **Step 3: Escrever o teste ponta a ponta**

`tests/PpLint.Cli.Tests/EndToEndTests.cs`:

```csharp
using System.IO.Compression;
using System.Text;

namespace PpLint.Cli.Tests;

public class EndToEndTests
{
    private const string ControlsJson = """
    {
      "TopParent": {
        "Name": "Screen1",
        "Template": { "Name": "screen" },
        "Children": [
          {
            "Name": "Button1",
            "Template": { "Name": "button" },
            "Rules": [ { "Property": "OnSelect", "InvariantScript": "Set(varNaoUsada, 1)" } ],
            "Children": []
          }
        ]
      }
    }
    """;

    private static string CreateMsapp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-e2e-{Guid.NewGuid():N}.msapp");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("Controls/1.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(ControlsJson);
        return path;
    }

    private static (int Code, string Out, string Err) Invoke(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Check_OnAppWithProblemsReportsAndExitsWithOne()
    {
        var (code, output, _) = Invoke("check", CreateMsapp(), "--no-color");

        Assert.Equal(1, code);
        Assert.Contains("NM010", output);   // Screen1 e Button1 com nome padrão
        Assert.Contains("PF101", output);   // varNaoUsada nunca lida
        Assert.Contains("Conformidade geral", output);
    }

    [Fact]
    public void Check_FailOnInfoStillExitsWithOne()
    {
        var (code, _, _) = Invoke("check", CreateMsapp(), "--fail-on", "info", "--no-color");
        Assert.Equal(1, code);
    }

    [Fact]
    public void Check_MissingFileExitsWithTwo()
    {
        var (code, _, err) = Invoke("check", "nao-existe.zip");

        Assert.Equal(2, code);
        Assert.Contains("nao-existe.zip", err);
    }

    [Fact]
    public void Check_CorruptArchiveExitsWithTwo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-corrupt-{Guid.NewGuid():N}.msapp");
        File.WriteAllText(path, "isto nao e um zip");

        var (code, _, err) = Invoke("check", path);

        Assert.Equal(2, code);
        Assert.Contains("não é um pacote zip válido", err);
    }

    [Fact]
    public void Check_EmptyPackageIsCleanAndExitsWithZero()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-vazio-{Guid.NewGuid():N}.msapp");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Header.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("{}");
        }

        var (code, output, _) = Invoke("check", path, "--no-color");

        Assert.Equal(0, code);
        Assert.Contains("Nenhum achado", output);
        Assert.Contains("100,0%", output);
    }

    [Fact]
    public void Check_QuietOmitsDiagnosticsButKeepsCompliance()
    {
        var (_, output, _) = Invoke("check", CreateMsapp(), "--quiet", "--no-color");

        Assert.DoesNotContain("NM010", output);
        Assert.Contains("Conformidade geral", output);
    }

    [Fact]
    public void Check_DoesNotModifyTheArtifact()
    {
        var path = CreateMsapp();
        var before = File.ReadAllBytes(path);
        var writtenBefore = File.GetLastWriteTimeUtc(path);

        Invoke("check", path, "--no-color");

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void UnknownCommandExitsWithTwo()
    {
        var (code, _, err) = Invoke("frobnicate");

        Assert.Equal(2, code);
        Assert.Contains("frobnicate", err);
    }

    [Fact]
    public void HelpExitsWithZero()
    {
        var (code, output, _) = Invoke("--help");

        Assert.Equal(0, code);
        Assert.Contains("pp-lint check", output);
    }

    [Fact]
    public void RulesListsThePhaseOneCatalog()
    {
        var (code, output, _) = Invoke("rules");

        Assert.Equal(0, code);
        Assert.Contains("NM010", output);
        Assert.Contains("FL201", output);
    }

    [Fact]
    public void UnsupportedFormatExitsWithTwo()
    {
        var (code, _, err) = Invoke("check", CreateMsapp(), "--format", "json");

        Assert.Equal(2, code);
        Assert.Contains("Fase 2", err);
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test tests/PpLint.Cli.Tests --filter EndToEndTests`
Expected: PASSA — 11 testes.

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test`
Expected: PASSA — todos os projetos de teste, sem avisos (o build trata aviso como erro).

- [ ] **Step 6: Criar o workflow de release**

`.github/workflows/release.yml`:

```yaml
name: release

on:
  push:
    tags: ["v*"]
  workflow_dispatch:

permissions:
  contents: write

jobs:
  build:
    strategy:
      fail-fast: false
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
            asset: pp-lint-win-x64.exe
            binary: pp-lint.exe
          - os: ubuntu-latest
            rid: linux-x64
            asset: pp-lint-linux-x64
            binary: pp-lint
          - os: macos-latest
            rid: osx-arm64
            asset: pp-lint-osx-arm64
            binary: pp-lint
          - os: macos-13
            rid: osx-x64
            asset: pp-lint-osx-x64
            binary: pp-lint
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Testes
        run: dotnet test --configuration Release

      - name: Publicar binário
        run: >
          dotnet publish src/PpLint.Cli
          --configuration Release
          --runtime ${{ matrix.rid }}
          --self-contained true
          -p:PublishSingleFile=true
          -p:PublishTrimmed=true
          -p:EnableCompressionInSingleFile=true
          --output publish

      - name: Renomear e gerar checksum
        shell: bash
        run: |
          mkdir -p artifacts
          cp "publish/${{ matrix.binary }}" "artifacts/${{ matrix.asset }}"
          cd artifacts
          shasum -a 256 "${{ matrix.asset }}" > "${{ matrix.asset }}.sha256"

      - uses: actions/upload-artifact@v4
        with:
          name: ${{ matrix.rid }}
          path: artifacts/*

  release:
    needs: build
    runs-on: ubuntu-latest
    if: startsWith(github.ref, 'refs/tags/v')
    steps:
      - uses: actions/download-artifact@v4
        with:
          path: artifacts
      - uses: softprops/action-gh-release@v2
        with:
          files: artifacts/**/*
          generate_release_notes: true
```

> `PublishTrimmed` reduz o binário mas pode remover tipos usados por reflexão. O `RuleEngine.CreateDefault` descobre regras por reflexão sobre `PpLint.Rules` — o Step 8 valida o binário publicado justamente para pegar isso. Se alguma regra sumir do `pp-lint rules` no binário publicado, remova `-p:PublishTrimmed=true` e registre a decisão no README.

- [ ] **Step 7: Criar o workflow de CI**

`.github/workflows/ci.yml`:

```yaml
name: ci

on:
  push:
    branches: [main]
  pull_request:

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet test --configuration Release
```

- [ ] **Step 8: Validar o binário publicado localmente**

```bash
cd /c/PROJETOS/pp-lint
dotnet publish src/PpLint.Cli --configuration Release --runtime win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=true --output publish
./publish/pp-lint.exe rules
./publish/pp-lint.exe --help
```

Expected: `rules` lista as 5 regras (NM010, NM011, PF101, PF110, FL201). Se a lista vier vazia ou incompleta, o trimming removeu as regras — remova `-p:PublishTrimmed=true` de ambos os locais (comando local e `release.yml`) e repita.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "test: contrato do catálogo e teste ponta a ponta; ci: workflows de build e release"
```

---

### Task 17: Validação contra artefato real

**Files:**
- Create: `tests/fixtures/README.md`
- Test: `tests/PpLint.Extractors.Tests/RealArtifactTests.cs`

**Interfaces:**
- Consumes: `ProjectLoader`, `RuleEngine.CreateDefault`.
- Produces: nenhuma API nova.

> **Esta task depende de um insumo humano.** Todos os testes até aqui usam artefatos sintéticos construídos a partir da documentação do formato. Eles provam que o código faz o que projetamos, não que o formato real é como projetamos. Peça ao dono do projeto **uma solução `.zip` exportada de verdade** (de preferência com ao menos um canvas app e um cloud flow) e coloque-a em `tests/fixtures/`. Sem isso, a Fase 1 não pode ser declarada concluída — o risco de o `MsappExtractor` não achar nada num `.msapp` real é o maior risco desta fase inteira.

- [ ] **Step 1: Documentar a expectativa de fixtures**

`tests/fixtures/README.md`:

```markdown
# Fixtures de artefatos reais

Coloque aqui soluções exportadas reais, com dados anonimizados, para validar
os extractors contra o formato de verdade — e não apenas contra os JSONs
sintéticos usados nos testes unitários.

Convenção de nomes:

- `solucao-exemplo.zip` — solução exportada com ao menos um canvas app e um cloud flow.

Os testes que dependem destes arquivos são pulados automaticamente quando eles
não existem, para que a suíte continue verde em quem clonou o repositório sem
os fixtures.

Antes de commitar um fixture: remova nomes de pessoas, e-mails, URLs de
ambiente e qualquer dado de cliente.
```

- [ ] **Step 2: Escrever o teste condicional**

`tests/PpLint.Extractors.Tests/RealArtifactTests.cs`:

```csharp
using PpLint.Core.Rules;
using PpLint.Rules.Naming;

namespace PpLint.Extractors.Tests;

/// <summary>
/// Valida os extractors contra uma solução exportada de verdade.
/// Pulado automaticamente quando o fixture não está presente.
/// </summary>
public class RealArtifactTests
{
    private static string? FixturePath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "solucao-exemplo.zip");
        var full = Path.GetFullPath(candidate);
        return File.Exists(full) ? full : null;
    }

    [SkippableFact]
    public void Load_RealSolutionExtractsAppsOrFlows()
    {
        var path = FixturePath();
        Skip.If(path is null, "Fixture tests/fixtures/solucao-exemplo.zip não encontrado.");

        var project = ProjectLoader.Load(path!);

        Assert.NotNull(project.Solution);
        Assert.True(
            project.Apps.Count > 0 || project.Flows.Count > 0,
            "Nenhum app nem fluxo foi extraído da solução real — o formato diverge do esperado.");
    }

    [SkippableFact]
    public void Load_RealSolutionAppHasControlsAndFormulas()
    {
        var path = FixturePath();
        Skip.If(path is null, "Fixture não encontrado.");

        var project = ProjectLoader.Load(path!);
        Skip.If(project.Apps.Count == 0, "A solução de exemplo não contém canvas apps.");

        var app = project.Apps[0];
        Assert.NotEmpty(app.Screens);
        Assert.True(
            app.AllControls().Any(c => c.Properties.Count > 0),
            "Nenhum controle trouxe fórmulas Power Fx — verifique o caminho Controls/*.json e o campo InvariantScript.");
    }

    [SkippableFact]
    public void Rules_RunOnRealSolutionWithoutCrashing()
    {
        var path = FixturePath();
        Skip.If(path is null, "Fixture não encontrado.");

        var project = ProjectLoader.Load(path!);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        Assert.Equal(5, result.Tallies.Count);
        Assert.True(
            result.Tallies.Any(t => t.Evaluated > 0),
            "Nenhuma regra examinou alvo algum numa solução real.");
    }
}
```

- [ ] **Step 3: Instalar o pacote de skip condicional e as referências**

```bash
cd /c/PROJETOS/pp-lint
dotnet add tests/PpLint.Extractors.Tests package Xunit.SkippableFact
dotnet add tests/PpLint.Extractors.Tests reference src/PpLint.Rules
```

- [ ] **Step 4: Rodar sem o fixture e confirmar que os testes são pulados**

Run: `dotnet test tests/PpLint.Extractors.Tests --filter RealArtifactTests`
Expected: 3 testes **pulados**, nenhum falhando.

- [ ] **Step 5: Rodar com o fixture real**

Colocar a solução exportada fornecida em `tests/fixtures/solucao-exemplo.zip` e rodar novamente.

Run: `dotnet test tests/PpLint.Extractors.Tests --filter RealArtifactTests`
Expected: 3 testes passando.

Se algum falhar, **não ajuste o teste**: inspecione o artefato real e corrija o extractor.

```bash
unzip -l tests/fixtures/solucao-exemplo.zip | head -50
dotnet run --project src/PpLint.Cli -- check tests/fixtures/solucao-exemplo.zip --no-color
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "test: validação dos extractors contra solução exportada real"
```

---

## Cobertura do spec nesta fase

Itens do spec **entregues** na Fase 1: leitura nativa de `.zip`/`.msapp`/pasta; IR completo; extração de controles, fórmulas, data sources, tabelas Dataverse e cloud flows; motor de regras com contagem de alvos; índice de conformidade global e por categoria; relatório de terminal; exit codes; 5 regras piloto; distribuição por GitHub Releases.

Itens do spec **deliberadamente adiados**, cada um para a fase indicada no plano de faseamento:

| Item do spec | Fase |
|---|---|
| Configuração via `pp-lint.toml` (defaults ficam embutidos em `PpLintConfig` na Fase 1) | 2 |
| Formatos JSON, SARIF, HTML e Markdown | 2 e 5 |
| Índice de conformidade **por artefato** (exige `RuleTally` por artefato) | 2 |
| Supressão inline (`// pp-lint: disable=`) e `per-artifact-ignores` | 2 |
| `pp-lint explain` com documentação real das regras | 2 |
| `pp-lint inspect` | 2 |
| Ingestão do `AppCheckerResult.sarif` | 2 |
| Catálogo completo NM/PF/FL/DUP/PERF/SEC/SOL | 2 a 4 |
| Baseline (`pp-lint baseline`) | 5 |
| Benchmark de performance | 5 |

## Execução

**Plano completo e salvo em `docs/superpowers/plans/2026-08-18-pp-lint-fase-1.md`. Duas opções de execução:**

**1. Subagent-Driven (recomendada)** — despacho um subagente novo por task, com revisão entre elas e iteração rápida.

**2. Execução inline** — executo as tasks nesta sessão via executing-plans, em lotes com checkpoints de revisão.

**Qual abordagem?**
