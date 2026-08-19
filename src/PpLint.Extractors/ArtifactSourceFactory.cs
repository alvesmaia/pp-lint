namespace PpLint.Extractors;

public static class ArtifactSourceFactory
{
    public static IArtifactSource Open(string path)
    {
        if (Directory.Exists(path))
            return new DirectoryArtifactSource(path);

        if (!File.Exists(path))
            throw new ArtifactException($"Caminho não encontrado: '{path}'.");

        return ZipArtifactSource.OpenFile(path);
    }
}
