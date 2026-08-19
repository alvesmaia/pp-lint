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
