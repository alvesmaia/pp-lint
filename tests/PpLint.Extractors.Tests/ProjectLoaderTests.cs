namespace PpLint.Extractors.Tests;

public class ProjectLoaderTests
{
    private const string ControlsJson =
        """{ "TopParent": { "Name": "scrHome", "Template": { "Name": "screen" }, "Children": [] } }""";

    [Fact]
    public void Load_MsappFileProducesSingleApp()
    {
        var msapp = TestZip.Create(("Controls/1.json", ControlsJson));
        var renamed = Path.ChangeExtension(msapp, ".msapp");
        File.Move(msapp, renamed);

        var project = ProjectLoader.Load(renamed);

        var app = Assert.Single(project.Apps);
        Assert.Single(app.Screens);
        Assert.Null(project.Solution);
    }

    [Fact]
    public void Load_SolutionZipProducesSolutionInfo()
    {
        const string solutionXml = """
        <ImportExportXml><SolutionManifest><UniqueName>S</UniqueName><Version>1.0</Version>
        <Managed>0</Managed><Publisher><CustomizationPrefix>abc</CustomizationPrefix></Publisher>
        </SolutionManifest></ImportExportXml>
        """;
        var zip = TestZip.Create(("solution.xml", solutionXml));
        var project = ProjectLoader.Load(zip);

        Assert.Equal("abc", project.Solution!.PublisherPrefix);
    }

    [Fact]
    public void Load_SetsSourcePath()
    {
        var zip = TestZip.Create(("solution.xml", "<ImportExportXml><SolutionManifest/></ImportExportXml>"));
        Assert.Equal(zip, ProjectLoader.Load(zip).SourcePath);
    }

    [Fact]
    public void Load_MissingPathThrowsArtifactException()
    {
        Assert.Throws<ArtifactException>(() => ProjectLoader.Load("nao-existe.zip"));
    }
}
