using System.Text.RegularExpressions;

namespace PpLint.Core.Rules;

/// <summary>
/// Decide quais regras rodam. Um item de select/ignore casa com o ID inteiro
/// (PF101) ou com a categoria (PF); ignore sempre vence select.
/// </summary>
public static class RuleFilter
{
    public static bool ShouldRun(string ruleId, PpLintConfig config)
    {
        if (Matches(ruleId, config.Ignore))
            return false;

        return config.Select.Count == 0 || Matches(ruleId, config.Select);
    }

    public static bool IsIgnoredForArtifact(string ruleId, string artifactPath, PpLintConfig config)
    {
        if (config.PerArtifactIgnores.Count == 0)
            return false;

        var normalized = artifactPath.Replace('\\', '/');

        foreach (var (glob, ruleIds) in config.PerArtifactIgnores)
        {
            if (!GlobMatches(glob, normalized))
                continue;

            if (Matches(ruleId, new HashSet<string>(ruleIds, StringComparer.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    /// <summary>O ID casa com ele mesmo ou com sua categoria — a parte alfabética inicial.</summary>
    private static bool Matches(string ruleId, IReadOnlySet<string> patterns)
    {
        if (patterns.Contains(ruleId))
            return true;

        var category = new string(ruleId.TakeWhile(char.IsAsciiLetter).ToArray());
        return category.Length > 0 && patterns.Contains(category);
    }

    private static bool GlobMatches(string glob, string path)
    {
        var pattern = "^" + Regex.Escape(glob)
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", ".") + "$";

        return Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
