using System.IO.Compression;
using System.Text;

namespace PpLint.Cli.Tests;

public class ConfigIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pplint-int-{Guid.NewGuid():N}");

    public ConfigIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private const string ControlsJson = """
    {
      "TopParent": {
        "Name": "scrHome",
        "Template": { "Name": "screen" },
        "Children": [
          { "Name": "SalvarPedido", "Template": { "Name": "button" }, "Children": [] },
          { "Name": "ButtonCancelar", "Template": { "Name": "button" }, "Children": [] }
        ]
      }
    }
    """;

    private string CreateApp()
    {
        var path = Path.Combine(_dir, "App.msapp");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("Controls/1.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(ControlsJson);
        return path;
    }

    private static (int Code, string Out, string Err) Invoke(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void WithoutConfig_UsesCamelPrefixAndFlagsPascalName()
    {
        var (_, output, _) = Invoke("check", CreateApp(), "--no-color");

        // 'ButtonCancelar' não começa com 'btn' no preset default.
        Assert.Contains("ButtonCancelar", output);
        Assert.Contains("SalvarPedido", output);
    }

    [Fact]
    public void PascalTypePreset_ChangesWhatIsFlagged()
    {
        File.WriteAllText(Path.Combine(_dir, "pp-lint.toml"), """
            [pp-lint]
            preset = "pascal-type"
            """);

        var (_, output, _) = Invoke("check", CreateApp(), "--no-color", "--config", Path.Combine(_dir, "pp-lint.toml"));

        // Agora 'ButtonCancelar' está correto e 'SalvarPedido' é que destoa.
        Assert.DoesNotContain("'ButtonCancelar'", output);
        Assert.Contains("SalvarPedido", output);
    }

    [Fact]
    public void IgnoreFromCommandLineSilencesRule()
    {
        var (_, output, _) = Invoke("check", CreateApp(), "--no-color", "--ignore", "NM011");

        Assert.DoesNotContain("NM011", output);
    }

    [Fact]
    public void SelectRunsOnlyTheChosenCategory()
    {
        var (_, output, _) = Invoke("check", CreateApp(), "--no-color", "--select", "PF");

        Assert.DoesNotContain("NM011", output);
    }

    [Fact]
    public void InvalidConfigExitsWithTwoAndExplains()
    {
        var config = Path.Combine(_dir, "quebrado.toml");
        File.WriteAllText(config, "[pp-lint\npreset =");

        var (code, _, err) = Invoke("check", CreateApp(), "--config", config);

        Assert.Equal(2, code);
        Assert.Contains("quebrado.toml", err);
    }

    [Fact]
    public void UnknownPresetExitsWithTwo()
    {
        var config = Path.Combine(_dir, "pp-lint.toml");
        File.WriteAllText(config, """
            [pp-lint]
            preset = "inventado"
            """);

        var (code, _, err) = Invoke("check", CreateApp(), "--config", config);

        Assert.Equal(2, code);
        Assert.Contains("inventado", err);
    }

    [Fact]
    public void MissingConfigFileExitsWithTwo()
    {
        var (code, _, err) = Invoke("check", CreateApp(), "--config", Path.Combine(_dir, "nao-existe.toml"));

        Assert.Equal(2, code);
        Assert.Contains("nao-existe.toml", err);
    }

    [Fact]
    public void WarnsAboutDefaultPresetWhenThereIsNoConfig()
    {
        var (_, _, err) = Invoke("check", CreateApp(), "--no-color");

        Assert.Contains("camel-prefix", err);
        Assert.Contains("pp-lint.toml", err);
    }

    [Fact]
    public void DoesNotWarnWhenConfigExists()
    {
        var config = Path.Combine(_dir, "pp-lint.toml");
        File.WriteAllText(config, """
            [pp-lint]
            preset = "camel-prefix"
            """);

        var (_, _, err) = Invoke("check", CreateApp(), "--no-color", "--config", config);

        Assert.DoesNotContain("camel-prefix", err);
    }

    [Fact]
    public void SuppressionDirectiveSilencesFinding()
    {
        var path = Path.Combine(_dir, "Suprimido.msapp");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Controls/1.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("""
            {
              "TopParent": {
                "Name": "scrHome",
                "Template": { "Name": "screen" },
                "Children": [
                  {
                    "Name": "SalvarPedido",
                    "Template": { "Name": "button" },
                    "Rules": [
                      { "Property": "OnSelect", "InvariantScript": "// pp-lint: disable=NM011\nNotify(\"ok\")" }
                    ],
                    "Children": []
                  }
                ]
              }
            }
            """);
        }

        var (_, output, _) = Invoke("check", path, "--no-color");

        Assert.DoesNotContain("NM011", output);
    }
}
