using Tomlyn;
using Tomlyn.Model;

namespace PpLint.Core.Configuration;

/// <summary>
/// Lê pp-lint.toml. Erros são sempre explícitos: configuração ignorada em
/// silêncio faz o time acreditar que configurou algo que não vale.
/// </summary>
public static class TomlConfigReader
{
    public static ConfigFile Read(string toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
            return ConfigFile.Empty;

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(toml)
                   ?? throw new ConfigException("TOML vazio ou não reconhecido.");
        }
        catch (TomlException ex)
        {
            throw new ConfigException($"TOML inválido: {ex.Message}", ex);
        }

        if (root.TryGetValue("pp-lint", out var raw) && raw is not TomlTable)
            throw new ConfigException("A seção [pp-lint] precisa ser uma tabela.");

        var section = raw as TomlTable ?? new TomlTable();
        var naming = GetTable(section, "naming");

        return new ConfigFile
        {
            Preset = GetString(section, "preset"),
            Select = GetStringList(section, "select"),
            Ignore = GetStringList(section, "ignore"),
            FailOn = GetSeverity(section, "fail-on"),
            SeverityOverrides = GetSeverityMap(GetTable(section, "severity-overrides")),
            ControlPrefixes = GetStringMap(GetTable(naming, "control-prefixes")),
            NamingPatterns = GetNamingPatterns(naming),
            PerArtifactIgnores = GetListMap(GetTable(section, "per-artifact-ignores")),
        };
    }

    private static TomlTable? GetTable(TomlTable? parent, string key)
    {
        if (parent is null || !parent.TryGetValue(key, out var value))
            return null;

        return value as TomlTable
               ?? throw new ConfigException($"'{key}' precisa ser uma tabela.");
    }

    private static string? GetString(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
            return null;

        return value as string
               ?? throw new ConfigException($"'{key}' precisa ser um texto.");
    }

    private static IReadOnlyList<string>? GetStringList(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
            return null;

        if (value is not TomlArray array)
            throw new ConfigException($"'{key}' precisa ser uma lista, por exemplo ignore = [\"PF125\"].");

        return array.Select(item =>
            item as string ?? throw new ConfigException($"'{key}' só aceita textos.")).ToList();
    }

    private static Severity? GetSeverity(TomlTable? table, string key)
    {
        var text = GetString(table, key);
        return text is null ? null : ParseSeverity(text, key);
    }

    private static Severity ParseSeverity(string text, string context) => text.ToLowerInvariant() switch
    {
        "error" => Severity.Error,
        "warning" => Severity.Warning,
        "info" => Severity.Info,
        _ => throw new ConfigException(
            $"Severidade inválida em '{context}': '{text}'. Válidas: error, warning, info."),
    };

    private static IReadOnlyDictionary<string, Severity>? GetSeverityMap(TomlTable? table)
    {
        if (table is null)
            return null;

        var result = new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in table)
        {
            var text = value as string
                       ?? throw new ConfigException($"A severidade de '{key}' precisa ser um texto.");
            result[key] = ParseSeverity(text, key);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string>? GetStringMap(TomlTable? table)
    {
        if (table is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in table)
        {
            result[key] = value as string
                          ?? throw new ConfigException($"O valor de '{key}' precisa ser um texto.");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? GetListMap(TomlTable? table)
    {
        if (table is null)
            return null;

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in table)
        {
            if (value is not TomlArray array)
                throw new ConfigException($"'{key}' precisa ser uma lista de IDs de regra.");

            result[key] = array.Select(item =>
                item as string ?? throw new ConfigException($"'{key}' só aceita textos.")).ToList();
        }

        return result;
    }

    /// <summary>
    /// Chaves de nomenclatura que não são a tabela de prefixos: global-variable,
    /// context-variable, collection, screen, component.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? GetNamingPatterns(TomlTable? naming)
    {
        if (naming is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in naming)
        {
            if (value is TomlTable)
                continue;

            result[key] = value as string
                          ?? throw new ConfigException($"O padrão de '{key}' precisa ser um texto.");
        }

        return result.Count > 0 ? result : null;
    }
}
