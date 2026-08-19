namespace PpLint.Core.Configuration;

/// <summary>
/// Acha o pp-lint.toml subindo do diretório atual até a raiz, como fazem
/// ruff, ESLint e EditorConfig.
/// </summary>
public static class ConfigLocator
{
    public const string FileName = "pp-lint.toml";

    public static string? Find(string startDirectory)
    {
        if (!Directory.Exists(startDirectory))
            return null;

        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, FileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return null;
    }
}
