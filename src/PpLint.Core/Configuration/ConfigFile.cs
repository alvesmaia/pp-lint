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
