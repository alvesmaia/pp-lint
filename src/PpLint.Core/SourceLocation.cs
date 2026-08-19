using System.Text;

namespace PpLint.Core;

/// <summary>
/// Onde um achado vive dentro do artefato analisado.
/// <paramref name="ArtifactPath"/> é o caminho do arquivo passado na linha de comando;
/// <paramref name="EntryPath"/> é a entrada dentro do pacote; <paramref name="Symbol"/>
/// é o nome legível (controle, propriedade, ação de fluxo).
/// </summary>
public sealed record SourceLocation(
    string ArtifactPath,
    string EntryPath,
    string? Symbol,
    int Line,
    int Column)
{
    public override string ToString()
    {
        var sb = new StringBuilder(ArtifactPath);
        if (!string.IsNullOrEmpty(EntryPath))
            sb.Append(" > ").Append(EntryPath);
        if (!string.IsNullOrEmpty(Symbol))
            sb.Append(" > ").Append(Symbol);
        if (Line > 0)
            sb.Append(':').Append(Line).Append(':').Append(Column);
        return sb.ToString();
    }
}
