using System.Diagnostics;
using PpLint.Core;
using PpLint.Core.Configuration;
using PpLint.Core.Model;
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
                stdout.WriteLine($"Documentação da regra {options.ExplainRuleId} disponível a partir da Fase 2.");
                return 0;

            case CliCommand.Check:
                return RunCheck(options, stdout, stderr, workingDirectory ?? Directory.GetCurrentDirectory());

            default:
                stderr.WriteLine("Comando não implementado.");
                return 2;
        }
    }

    private static int RunCheck(
        CliOptions options, TextWriter stdout, TextWriter stderr, string workingDirectory)
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
            (config, usedConfigFile) = LoadConfig(options, workingDirectory);
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
          pp-lint explain <ID>                  documentação de uma regra
          pp-lint --version                     mostra a versão

        Opções:
          --config <arquivo>                    usa este pp-lint.toml em vez de procurar
          --select <IDs>                        só estas regras (ID ou categoria, separados por vírgula)
          --ignore <IDs>                        nunca estas regras; vence o --select
          --format <text|json|sarif|html|md>    formato de saída (padrão: text)
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
