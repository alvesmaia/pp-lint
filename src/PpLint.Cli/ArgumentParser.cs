using PpLint.Core;

namespace PpLint.Cli;

public enum CliCommand { Check, Explain, Rules, Version, Help, Baseline }

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
    string? ConfigPath,
    string? BaselinePath);

public sealed record ParseResult<T>(bool IsSuccess, T? Value, string? Error)
{
    public static ParseResult<T> Ok(T value) => new(true, value, null);
    public static ParseResult<T> Fail(string error) => new(false, default, error);
}

public static class ArgumentParser
{
    private static readonly string[] ValidFormats = ["text", "json", "sarif", "html"];

    public static ParseResult<CliOptions> Parse(string[] args)
    {
        if (args.Length == 0)
            return Ok(CliCommand.Help);

        var command = args[0] switch
        {
            "check" => CliCommand.Check,
            "explain" => CliCommand.Explain,
            "rules" => CliCommand.Rules,
            "baseline" => CliCommand.Baseline,
            "--version" or "-v" or "version" => CliCommand.Version,
            "--help" or "-h" or "help" => CliCommand.Help,
            _ => (CliCommand?)null,
        };

        if (command is null)
            return ParseResult<CliOptions>.Fail($"Comando desconhecido: '{args[0]}'. Use 'pp-lint --help'.");

        var paths = new List<string>();
        var select = new List<string>();
        var ignore = new List<string>();
        var format = "text";
        string? output = null;
        string? configPath = null;
        string? baselinePath = null;
        Severity? failOn = null;
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
                case "--select":
                case "--ignore":
                case "--config":
                case "--baseline":
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
                        case "--baseline":
                            baselinePath = value;
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
            command.Value, paths, format, output, failOn, noColor, quiet, explainRuleId,
            select, ignore, configPath, baselinePath));
    }

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Severity? ParseSeverity(string value) => value.ToLowerInvariant() switch
    {
        "error" => Severity.Error,
        "warning" => Severity.Warning,
        "info" => Severity.Info,
        _ => null,
    };

    private static ParseResult<CliOptions> Ok(CliCommand command) =>
        ParseResult<CliOptions>.Ok(new CliOptions(
            command, [], "text", null, null, false, false, null, [], [], null, null));
}
