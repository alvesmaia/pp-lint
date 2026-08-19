namespace PpLint.Extractors;

/// <summary>
/// Acesso somente-leitura ao conteúdo de um artefato — um pacote zip
/// (.zip / .msapp) ou uma pasta descompactada.
/// Todos os caminhos de entrada usam '/' e são comparados sem diferenciar maiúsculas.
/// </summary>
public interface IArtifactSource : IDisposable
{
    /// <summary>Caminho do artefato, como exibido nos diagnósticos.</summary>
    string Path { get; }

    IEnumerable<string> Entries { get; }

    bool Has(string entry);

    string ReadText(string entry);

    Stream OpenRead(string entry);

    /// <summary>
    /// Abre um pacote aninhado (ex.: um .msapp dentro de uma solução).
    /// Retorna null quando a entrada existe mas não é um pacote válido.
    /// </summary>
    IArtifactSource? OpenNested(string entry);
}

public sealed class ArtifactException : Exception
{
    public ArtifactException(string message) : base(message) { }
    public ArtifactException(string message, Exception inner) : base(message, inner) { }
}

internal static class EntryPath
{
    public static string Normalize(string entry) => entry.Replace('\\', '/').TrimStart('/');
}
