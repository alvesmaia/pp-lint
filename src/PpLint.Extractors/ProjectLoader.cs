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
