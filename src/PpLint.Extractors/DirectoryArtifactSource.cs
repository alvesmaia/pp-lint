using System.Text;

namespace PpLint.Extractors;

public sealed class DirectoryArtifactSource : IArtifactSource
{
    private readonly string _root;
    private readonly Dictionary<string, string> _entries;
    private readonly List<IDisposable> _nested = [];

    public DirectoryArtifactSource(string root)
    {
        _root = System.IO.Path.GetFullPath(root);
        Path = root;
        _entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(_root, file);
            _entries[EntryPath.Normalize(relative)] = file;
        }
    }

    public string Path { get; }

    public IEnumerable<string> Entries => _entries.Keys;

    public bool Has(string entry) => _entries.ContainsKey(EntryPath.Normalize(entry));

    public Stream OpenRead(string entry)
    {
        var key = EntryPath.Normalize(entry);
        if (!_entries.TryGetValue(key, out var full))
            throw new ArtifactException($"Entrada não encontrada em '{Path}': {entry}");
        return File.OpenRead(full);
    }

    public string ReadText(string entry)
    {
        using var stream = OpenRead(entry);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public IArtifactSource? OpenNested(string entry)
    {
        var buffer = new MemoryStream();
        using (var stream = OpenRead(entry))
            stream.CopyTo(buffer);
        buffer.Position = 0;

        var nested = ZipArtifactSource.TryOpenStream(EntryPath.Normalize(entry), buffer);
        if (nested is not null)
            _nested.Add(nested);
        return nested;
    }

    public void Dispose()
    {
        foreach (var n in _nested)
            n.Dispose();
    }
}
