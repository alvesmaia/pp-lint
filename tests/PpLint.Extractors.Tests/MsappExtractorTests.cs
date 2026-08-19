using PpLint.Core.Model;

namespace PpLint.Extractors.Tests;

public class MsappExtractorTests
{
    private const string ControlsJson = """
    {
      "TopParent": {
        "Name": "scrPedidos",
        "Template": { "Name": "screen" },
        "Rules": [
          { "Property": "OnVisible", "InvariantScript": "Set(varTotal; 0)" }
        ],
        "Children": [
          {
            "Name": "btnSalvar",
            "Template": { "Name": "button" },
            "Rules": [
              { "Property": "OnSelect", "InvariantScript": "Notify(\"ok\")" },
              { "Property": "Text", "InvariantScript": "\"Salvar\"" }
            ],
            "Children": []
          },
          {
            "Name": "Label1",
            "Template": { "Name": "label" },
            "Children": []
          }
        ]
      }
    }
    """;

    private const string DataSourcesJson = """
    {
      "DataSources": [
        {
          "Name": "Pedidos",
          "Type": "OptionSetInfo",
          "Columns": [ { "Name": "Title" }, { "Name": "Data de Entrega" } ]
        }
      ]
    }
    """;

    [Fact]
    public void Extract_ReadsScreenAndControlTree()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        Assert.Equal("AppVendas", app.Name);
        var screen = Assert.Single(app.Screens);
        Assert.Equal("scrPedidos", screen.Name);
        Assert.True(screen.IsScreen);
        Assert.Equal(["scrPedidos", "btnSalvar", "Label1"], app.AllControls().Select(c => c.Name));
    }

    [Fact]
    public void Extract_ReadsTemplateNames()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        var button = app.AllControls().Single(c => c.Name == "btnSalvar");
        Assert.Equal("button", button.TemplateName);
    }

    [Fact]
    public void Extract_ReadsPowerFxProperties()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        var button = app.AllControls().Single(c => c.Name == "btnSalvar");
        Assert.Equal(2, button.Properties.Count);
        var onSelect = button.Properties.Single(p => p.Name == "OnSelect");
        Assert.Equal("Notify(\"ok\")", onSelect.Script);
        Assert.Equal("btnSalvar.OnSelect", onSelect.Location.Symbol);
        Assert.Equal("Controls/1.json", onSelect.Location.EntryPath);
    }

    [Fact]
    public void Extract_ControlWithoutRulesHasNoProperties()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        Assert.Empty(app.AllControls().Single(c => c.Name == "Label1").Properties);
    }

    [Fact]
    public void Extract_ReadsDataSourcesWithColumns()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson), ("DataSources/DataSources.json", DataSourcesJson));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        var ds = Assert.Single(app.DataSources);
        Assert.Equal("Pedidos", ds.Name);
        Assert.Equal(["Title", "Data de Entrega"], ds.Columns);
    }

    [Fact]
    public void Extract_ReadsAppOnStartFromProperties()
    {
        const string props = """
        { "LocalConnectionReferences": "", "OnStart": "Set(varUsuario; User().Email)" }
        """;
        var zip = TestZip.Create(("Controls/1.json", ControlsJson), ("Properties.json", props));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        var onStart = Assert.Single(app.AppProperties);
        Assert.Equal("OnStart", onStart.Name);
        Assert.Equal("Set(varUsuario; User().Email)", onStart.Script);
        Assert.Equal("App.OnStart", onStart.Location.Symbol);
    }

    [Fact]
    public void Extract_MultipleControlFilesProduceMultipleScreens()
    {
        const string second = """
        { "TopParent": { "Name": "scrDetalhe", "Template": { "Name": "screen" }, "Children": [] } }
        """;
        var zip = TestZip.Create(("Controls/1.json", ControlsJson), ("Controls/2.json", second));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        Assert.Equal(2, app.Screens.Count);
        Assert.Contains(app.Screens, s => s.Name == "scrDetalhe");
    }

    [Fact]
    public void Extract_MalformedControlFileIsSkipped()
    {
        var zip = TestZip.Create(("Controls/1.json", ControlsJson), ("Controls/2.json", "{ isto nao e json"));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVendas");

        Assert.Single(app.Screens);
    }

    [Fact]
    public void Extract_EmptyPackageProducesEmptyApp()
    {
        var zip = TestZip.Create(("Header.json", "{}"));
        using var src = ArtifactSourceFactory.Open(zip);
        var app = MsappExtractor.Extract(src, "App.msapp", "AppVazio");

        Assert.Empty(app.Screens);
        Assert.Empty(app.DataSources);
    }
}
