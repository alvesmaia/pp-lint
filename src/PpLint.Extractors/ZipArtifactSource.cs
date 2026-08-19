using System.IO.Compression;
using System.Text;

namespace PpLint.Extractors;

public sealed class ZipArtifactSource : IArtifactSource
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly Stream? _ownedStream;
    private readonly List<IDisposable> _nested = [];

    private ZipArtifactSource(string path, ZipArchive archive, Stream? ownedStream)
    {
        Path = path;
        _archive = archive;
        _ownedStream = ownedStream;
        _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in archive.Entries)
        {
            if (e.FullName.EndsWith('/'))
                continue;
            _entries[EntryPath.Normalize(e.FullName)] = e;
        }
    }

    public string Path { get; }

    public IEnumerable<string> Entries => _entries.Keys;

    public static ZipArtifactSource OpenFile(string path)
    {
        try
        {
            var stream = File.OpenRead(path);
            var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            return new ZipArtifactSource(path, archive, stream);
        }
        catch (InvalidDataException ex)
        {
            throw new ArtifactException($"O arquivo '{path}' não é um pacote zip válido.", ex);
        }
    }

    /// <summary>Abre um pacote a partir de um stream já carregado em memória.</summary>
    public static ZipArtifactSource? TryOpenStream(string logicalPath, Stream seekable)
    {
        try
        {
            var archive = new ZipArchive(seekable, ZipArchiveMode.Read, leaveOpen: false);
            return new ZipArtifactSource(logicalPath, archive, seekable);
        }
        catch (InvalidDataException)
        {
            seekable.Dispose();
            return null;
        }
    }

    public bool Has(string entry) => _entries.ContainsKey(EntryPath.Normalize(entry));

    public Stream OpenRead(string entry)
    {
        var key = EntryPath.Normalize(entry);
        if (!_entries.TryGetValue(key, out var e))
            throw new ArtifactException($"Entrada não encontrada em '{Path}': {entry}");
        return e.Open();
    }

    public string ReadText(string entry)
    {
        using var stream = OpenRead(entry);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public IArtifactSource? OpenNested(string entry)
    {
        // ZipArchiveEntry.Open() não é seekable; copiamos para memória antes de reabrir.
        var buffer = new MemoryStream();
        using (var stream = OpenRead(entry))
            stream.CopyTo(buffer);
        buffer.Position = 0;

        var nested = TryOpenStream(EntryPath.Normalize(entry), buffer);
        if (nested is not null)
            _nested.Add(nested);
        return nested;
    }

    public void Dispose()
    {
        foreach (var n in _nested)
            n.Dispose();
        _archive.Dispose();
        _ownedStream?.Dispose();
    }
}
