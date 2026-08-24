using System.Diagnostics;
using PpLint.Core;
using PpLint.Core.Configuration;
using PpLint.Core.Model;
using PpLint.Core.Baseline;
using PpLint.Core.Reporting;
using PpLint.Rules;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;
using PpLint.Core.Suppression;
using PpLint.Extractors;
using PpLint.Rules.Naming;

namespace PpLint.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        // As mensagens são em português; sem UTF-8 explícito o console do
        // Windows usa a code page do sistema e corrompe a acentuação.
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // Saída redirecionada para um handle que não aceita troca de encoding.
        }

        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>
    /// <paramref name="workingDirectory"/> é de onde a busca por pp-lint.toml começa.
    /// Injetável para que os testes não dependam do diretório do processo, que é
    /// global e compartilhado entre testes paralelos.
    /// </summary>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        string? workingDirectory = null)
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

            case CliCommand.Check:
                return RunCheck(options, stdout, stderr, workingDirectory ?? Directory.GetCurrentDirectory());

            case CliCommand.Baseline:
                return RunBaseline(options, stdout, stderr, workingDirectory ?? Directory.GetCurrentDirectory());

            default:
                stderr.WriteLine("Comando não implementado.");
                return 2;
        }
    }

    private static int RunCheck(
        CliOptions options, TextWriter stdout, TextWriter stderr, string workingDirectory)
    {
        PpLintConfig config;
        bool usedConfigFile;
        try
        {
            (config, usedConfigFile) = LoadConfig(options, workingDirectory);
        }
        catch (ConfigException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }

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

        PpLint.Core.Baseline.Baseline baseline;
        try
        {
            baseline = LoadBaseline(options, workingDirectory);
        }
        catch (BaselineException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }

        // A linha de base tira o achado antigo do relatório e do código de saída,
        // e não do índice: a conformidade continua contando o débito que existe.
        // Fosse o contrário, bastaria gerar uma linha de base para exibir 100%.
        var novos = baseline.Unbaselined(run.AllDiagnostics);
        var exibidos = baseline.Entries.Count == 0 ? run : run.ShowingOnly(novos);

        var saida = options.Format switch
        {
            "json" => JsonReporter.Render(exibidos),
            "sarif" => SarifReporter.Render(exibidos),
            "html" => HtmlReporter.Render(exibidos),
            _ => TextReporter.Render(exibidos, useColor, options.Quiet),
        };

        if (options.Output is not null)
            File.WriteAllText(options.Output, saida);
        else
            stdout.Write(saida);

        if (baseline.Entries.Count > 0)
        {
            var ocultos = run.AllDiagnostics.Count - novos.Count;
            stderr.WriteLine(
                $"Linha de base: {ocultos} {(ocultos == 1 ? "achado conhecido segue oculto" : "achados conhecidos seguem ocultos")}. "
                + "Eles continuam no índice de conformidade — a linha de base impede que quebrem o "
                + "build, não que contem.");
        }

        if (!usedConfigFile && run.AllDiagnostics.Any(d => d.Category == RuleCategory.Naming))
        {
            stderr.WriteLine(
                $"Nota: usando o preset de nomenclatura '{config.PresetName}' porque não há {ConfigLocator.FileName}. "
                + $"Presets disponíveis: {string.Join(", ", NamingPresets.Names)}. "
                + $"Crie um {ConfigLocator.FileName} com [pp-lint] preset = \"...\" para escolher outro.");
        }

        // Só o que a linha de base não cobre decide o código de saída. É isso
        // que permite ligar o linter num app legado sem parar o time.
        return novos.Any(d => d.Severity >= config.FailOn) ? 1 : 0;
    }

    private static int RunBaseline(
        CliOptions options, TextWriter stdout, TextWriter stderr, string workingDirectory)
    {
        PpLintConfig config;
        try
        {
            (config, _) = LoadConfig(options, workingDirectory);
        }
        catch (ConfigException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }

        var resultados = new List<(string Path, LintResult Result)>();
        var engine = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly);

        foreach (var path in options.Paths)
        {
            try
            {
                var project = ProjectLoader.Load(path);
                resultados.Add((path, engine.Run(project, config, SuppressionIndex.Build(project))));
            }
            catch (ArtifactException ex)
            {
                stderr.WriteLine($"Erro ao ler '{path}': {ex.Message}");
                return 2;
            }
            catch (ConfigException ex)
            {
                stderr.WriteLine(ex.Message);
                return 2;
            }
        }

        var run = AnalysisRun.From(resultados, TimeSpan.Zero);
        var baseline = PpLint.Core.Baseline.Baseline.From(run);
        var destino = options.Output
                      ?? options.BaselinePath
                      ?? Path.Combine(workingDirectory, PpLint.Core.Baseline.Baseline.DefaultFileName);

        File.WriteAllText(destino, baseline.ToJson());

        stdout.WriteLine(
            $"Linha de base gravada em {destino}: {baseline.Total} "
            + $"{(baseline.Total == 1 ? "achado" : "achados")} em "
            + $"{baseline.Entries.Count} {(baseline.Entries.Count == 1 ? "posição" : "posições")}.");
        stdout.WriteLine(
            "As execuções seguintes só falham em achado novo. O índice de conformidade continua "
            + "contando os antigos — a linha de base adia a dívida, não a apaga.");

        return 0;
    }

    /// <summary>
    /// A linha de base a aplicar. Sem --baseline, procura o arquivo padrão no
    /// diretório de trabalho: quem o commitou no repositório espera que valha
    /// sem repetir a opção em todo comando.
    /// </summary>
    private static PpLint.Core.Baseline.Baseline LoadBaseline(CliOptions options, string workingDirectory)
    {
        if (options.BaselinePath is not null)
        {
            if (!File.Exists(options.BaselinePath))
                throw new BaselineException($"Linha de base não encontrada: '{options.BaselinePath}'.");

            return PpLint.Core.Baseline.Baseline.Parse(File.ReadAllText(options.BaselinePath));
        }

        var padrao = Path.Combine(workingDirectory, PpLint.Core.Baseline.Baseline.DefaultFileName);

        return File.Exists(padrao)
            ? PpLint.Core.Baseline.Baseline.Parse(File.ReadAllText(padrao))
            : PpLint.Core.Baseline.Baseline.Empty;
    }

    /// <summary>
    /// Monta a configuração final e informa se algum arquivo foi de fato usado —
    /// é o que decide se vale avisar sobre o preset default.
    /// </summary>
    private static (PpLintConfig Config, bool UsedFile) LoadConfig(
        CliOptions options, string workingDirectory)
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
            path = ConfigLocator.Find(workingDirectory);
        }

        var file = ConfigFile.Empty;
        if (path is not null)
        {
            string texto;
            try
            {
                texto = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Sem isso, um arquivo travado ou sem permissão derruba o processo
                // com stack trace em vez de sair com código 2 e uma mensagem útil.
                throw new ConfigException($"Não foi possível ler '{path}': {ex.Message}", ex);
            }

            try
            {
                file = TomlConfigReader.Read(texto);
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

        try
        {
            return (ConfigResolver.Resolve(file, cli), path is not null);
        }
        catch (ConfigException ex) when (path is not null)
        {
            // Com um arquivo descoberto num diretório ancestral, dizer apenas
            // "preset desconhecido" deixa o usuário sem saber qual arquivo corrigir.
            throw new ConfigException($"Erro em '{path}': {ex.Message}", ex);
        }
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
          pp-lint baseline <caminho...>         grava os achados atuais como linha de base
          pp-lint explain <ID>                  documentação de uma regra
          pp-lint --version                     mostra a versão

        Opções:
          --config <arquivo>                    usa este pp-lint.toml em vez de procurar
          --baseline <arquivo>                  linha de base a aplicar (padrão: pp-lint-baseline.json)
          --select <IDs>                        só estas regras (ID ou categoria, separados por vírgula)
          --ignore <IDs>                        nunca estas regras; vence o --select
          --format <text|json|sarif|html>       formato de saída (padrão: text)
          --output <arquivo>                    grava a saída em arquivo
          --fail-on <error|warning|info>        severidade que retorna código 1 (padrão: error)
          --no-color                            desativa cores
          --quiet                               omite os achados individuais

        Sem --config, procura pp-lint.toml no diretório atual e nos ancestrais.

        Códigos de saída:
          0  nenhum achado no nível de --fail-on
          1  achados no nível de --fail-on ou acima
          2  erro de execução
        """;
}
