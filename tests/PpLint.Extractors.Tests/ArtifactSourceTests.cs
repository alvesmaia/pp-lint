namespace PpLint.Extractors.Tests;

public class ArtifactSourceTests
{
    [Fact]
    public void Zip_ListsEntriesAndReadsText()
    {
        var zip = TestZip.Create(("solution.xml", "<x/>"), ("Workflows/a.json", "{}"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.Contains("solution.xml", src.Entries);
        Assert.Contains("Workflows/a.json", src.Entries);
        Assert.Equal("<x/>", src.ReadText("solution.xml"));
    }

    [Fact]
    public void Zip_HasIsCaseInsensitive()
    {
        var zip = TestZip.Create(("Solution.xml", "<x/>"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.True(src.Has("solution.xml"));
        Assert.Equal("<x/>", src.ReadText("SOLUTION.XML"));
    }

    [Fact]
    public void Zip_NormalizesBackslashSeparators()
    {
        var zip = TestZip.Create((@"CanvasApps\App.msapp", "conteudo"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.Contains("CanvasApps/App.msapp", src.Entries);
    }

    [Fact]
    public void Zip_OpensNestedArchive()
    {
        var inner = TestZip.Create(("Controls/1.json", "{\"a\":1}"));
        var outer = TestZip.CreateNested("CanvasApps/App.msapp", inner, ("solution.xml", "<x/>"));
        using var src = ArtifactSourceFactory.Open(outer);
        using var nested = src.OpenNested("CanvasApps/App.msapp");
        Assert.NotNull(nested);
        Assert.Equal("{\"a\":1}", nested!.ReadText("Controls/1.json"));
        Assert.Equal("CanvasApps/App.msapp", nested.Path);
    }

    [Fact]
    public void Directory_ListsFilesRecursively()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pplint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "Src"));
        File.WriteAllText(Path.Combine(dir, "Src", "App.fx.yaml"), "conteudo");
        using var src = ArtifactSourceFactory.Open(dir);
        Assert.Contains("Src/App.fx.yaml", src.Entries);
        Assert.Equal("conteudo", src.ReadText("Src/App.fx.yaml"));
    }

    [Fact]
    public void MissingPath_ThrowsArtifactException()
    {
        var ex = Assert.Throws<ArtifactException>(() => ArtifactSourceFactory.Open("nao-existe.zip"));
        Assert.Contains("nao-existe.zip", ex.Message);
    }

    [Fact]
    public void CorruptZip_ThrowsArtifactException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-{Guid.NewGuid():N}.zip");
        File.WriteAllText(path, "isto nao e um zip");
        var ex = Assert.Throws<ArtifactException>(() => ArtifactSourceFactory.Open(path));
        Assert.Contains("não é um pacote zip válido", ex.Message);
    }

    [Fact]
    public void ReadingUnknownEntry_ThrowsArtifactException()
    {
        var zip = TestZip.Create(("a.txt", "x"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.Throws<ArtifactException>(() => src.ReadText("b.txt"));
    }

    [Fact]
    public void OpenNested_ReturnsNullForNonArchiveEntry()
    {
        var zip = TestZip.Create(("a.txt", "isto nao e um zip"));
        using var src = ArtifactSourceFactory.Open(zip);
        Assert.Null(src.OpenNested("a.txt"));
    }
}
