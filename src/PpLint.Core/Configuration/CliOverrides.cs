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
