using System.IO.Compression;
using System.Text;

namespace PpLint.Extractors.Tests;

/// <summary>Cria zips temporários em disco para os testes.</summary>
public static class TestZip
{
    public static string Create(params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var e = zip.CreateEntry(entry);
            using var writer = new StreamWriter(e.Open(), Encoding.UTF8);
            writer.Write(content);
        }
        return path;
    }

    public static string CreateNested(string innerEntry, string innerZipPath, params (string, string)[] outerEntries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (entry, content) in outerEntries)
        {
            var e = zip.CreateEntry(entry);
            using var writer = new StreamWriter(e.Open(), Encoding.UTF8);
            writer.Write(content);
        }
        var nested = zip.CreateEntry(innerEntry);
        using (var target = nested.Open())
        using (var source = File.OpenRead(innerZipPath))
            source.CopyTo(target);
        return path;
    }
}
