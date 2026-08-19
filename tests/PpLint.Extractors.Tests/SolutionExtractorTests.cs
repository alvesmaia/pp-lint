using PpLint.Core.Model;

namespace PpLint.Extractors.Tests;

public class SolutionExtractorTests
{
    private const string SolutionXml = """
    <?xml version="1.0" encoding="utf-8"?>
    <ImportExportXml version="9.2.0.0">
      <SolutionManifest>
        <UniqueName>MinhaSolucao</UniqueName>
        <Version>1.0.0.3</Version>
        <Managed>0</Managed>
        <Publisher>
          <UniqueName>contoso</UniqueName>
          <CustomizationPrefix>cts</CustomizationPrefix>
        </Publisher>
      </SolutionManifest>
    </ImportExportXml>
    """;

    private const string EntityXml = """
    <?xml version="1.0" encoding="utf-8"?>
    <Entity>
      <Name LocalizedName="Pedido">cts_pedido</Name>
      <EntityInfo>
        <entity Name="cts_pedido">
          <attributes>
            <attribute PhysicalName="cts_Titulo">
              <Type>nvarchar</Type>
              <LogicalName>cts_titulo</LogicalName>
              <RequiredLevel>required</RequiredLevel>
            </attribute>
            <attribute PhysicalName="ValorTotal">
              <Type>money</Type>
              <LogicalName>valortotal</LogicalName>
              <RequiredLevel>none</RequiredLevel>
            </attribute>
          </attributes>
        </entity>
      </EntityInfo>
    </Entity>
    """;

    private const string FlowJson = """
    { "definition": { "actions": { "Compose": { "type": "Compose", "inputs": "x" } } } }
    """;

    [Fact]
    public void Populate_ReadsSolutionManifest()
    {
        var zip = TestZip.Create(("solution.xml", SolutionXml));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        Assert.NotNull(project.Solution);
        Assert.Equal("MinhaSolucao", project.Solution!.UniqueName);
        Assert.Equal("cts", project.Solution.PublisherPrefix);
        Assert.Equal("1.0.0.3", project.Solution.Version);
        Assert.False(project.Solution.Managed);
    }

    [Fact]
    public void Populate_ReadsManagedFlag()
    {
        var zip = TestZip.Create(("solution.xml", SolutionXml.Replace("<Managed>0</Managed>", "<Managed>1</Managed>")));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        Assert.True(project.Solution!.Managed);
    }

    [Fact]
    public void Populate_ReadsEntityColumns()
    {
        var zip = TestZip.Create(("solution.xml", SolutionXml), ("Entities/cts_pedido/Entity.xml", EntityXml));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        var table = Assert.Single(project.Tables);
        Assert.Equal("cts_pedido", table.LogicalName);
        Assert.Equal(2, table.Columns.Count);

        var titulo = table.Columns.Single(c => c.LogicalName == "cts_titulo");
        Assert.Equal("cts_Titulo", titulo.SchemaName);
        Assert.Equal("nvarchar", titulo.Type);
        Assert.True(titulo.Required);

        Assert.False(table.Columns.Single(c => c.LogicalName == "valortotal").Required);
    }

    [Fact]
    public void Populate_ReadsWorkflowsAndDerivesNameFromFileName()
    {
        var zip = TestZip.Create(
            ("solution.xml", SolutionXml),
            ("Workflows/AprovarPedido-A1B2C3D4-1111-2222-3333-444455556666.json", FlowJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        var flow = Assert.Single(project.Flows);
        Assert.Equal("AprovarPedido", flow.Name);
    }

    [Fact]
    public void Populate_ReadsNestedCanvasApp()
    {
        var msapp = TestZip.Create(("Controls/1.json",
            """{ "TopParent": { "Name": "scrHome", "Template": { "Name": "screen" }, "Children": [] } }"""));
        var zip = TestZip.CreateNested("CanvasApps/AppVendas_DocumentUri.msapp", msapp, ("solution.xml", SolutionXml));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        var app = Assert.Single(project.Apps);
        Assert.Equal("AppVendas", app.Name);
        Assert.Single(app.Screens);
    }

    [Fact]
    public void Populate_MalformedEntityXmlIsSkipped()
    {
        var zip = TestZip.Create(("solution.xml", SolutionXml), ("Entities/x/Entity.xml", "<nao fechado"));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        Assert.Empty(project.Tables);
        Assert.NotNull(project.Solution);
    }

    [Fact]
    public void Populate_WithoutSolutionXmlLeavesSolutionNull()
    {
        var zip = TestZip.Create(("Workflows/F-11111111-1111-1111-1111-111111111111.json", FlowJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var project = new PowerPlatformProject { SourcePath = zip };
        SolutionExtractor.Populate(src, project);

        Assert.Null(project.Solution);
        Assert.Single(project.Flows);
    }
}
