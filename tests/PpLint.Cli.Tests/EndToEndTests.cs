using System.IO.Compression;
using System.Text;

namespace PpLint.Cli.Tests;

public class EndToEndTests
{
    private const string ControlsJson = """
    {
      "TopParent": {
        "Name": "Screen1",
        "Template": { "Name": "screen" },
        "Children": [
          {
            "Name": "Button1",
            "Template": { "Name": "button" },
            "Rules": [ { "Property": "OnSelect", "InvariantScript": "Set(varNaoUsada, 1)" } ],
            "Children": []
          }
        ]
      }
    }
    """;

    private static string CreateMsapp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-e2e-{Guid.NewGuid():N}.msapp");
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
    public void Check_OnAppWithProblemsReportsAndExitsWithOne()
    {
        var (code, output, _) = Invoke("check", CreateMsapp(), "--no-color");

        Assert.Equal(1, code);
        Assert.Contains("NM010", output);   // Screen1 e Button1 com nome padrão
        Assert.Contains("PF101", output);   // varNaoUsada nunca lida
        Assert.Contains("Conformidade geral", output);
    }

    [Fact]
    public void Check_FailOnInfoStillExitsWithOne()
    {
        var (code, _, _) = Invoke("check", CreateMsapp(), "--fail-on", "info", "--no-color");
        Assert.Equal(1, code);
    }

    [Fact]
    public void Check_MissingFileExitsWithTwo()
    {
        var (code, _, err) = Invoke("check", "nao-existe.zip");

        Assert.Equal(2, code);
        Assert.Contains("nao-existe.zip", err);
    }

    [Fact]
    public void Check_CorruptArchiveExitsWithTwo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-corrupt-{Guid.NewGuid():N}.msapp");
        File.WriteAllText(path, "isto nao e um zip");

        var (code, _, err) = Invoke("check", path);

        Assert.Equal(2, code);
        Assert.Contains("não é um pacote zip válido", err);
    }

    [Fact]
    public void Check_EmptyPackageIsCleanAndExitsWithZero()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pplint-vazio-{Guid.NewGuid():N}.msapp");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Header.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("{}");
        }

        var (code, output, _) = Invoke("check", path, "--no-color");

        Assert.Equal(0, code);
        Assert.Contains("Nenhum achado", output);
        Assert.Contains("100,0%", output);
    }

    [Fact]
    public void Check_QuietOmitsDiagnosticsButKeepsCompliance()
    {
        var (_, output, _) = Invoke("check", CreateMsapp(), "--quiet", "--no-color");

        // O achado detalhado some; o resumo com as ocorrências por regra permanece.
        Assert.DoesNotContain("mantém o nome padrão", output);
        Assert.DoesNotContain("Controls/1.json", output);
        Assert.Contains("Conformidade geral", output);
        Assert.Contains("Principais ocorrências", output);
    }

    [Fact]
    public void Check_DoesNotModifyTheArtifact()
    {
        var path = CreateMsapp();
        var before = File.ReadAllBytes(path);
        var writtenBefore = File.GetLastWriteTimeUtc(path);

        Invoke("check", path, "--no-color");

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void UnknownCommandExitsWithTwo()
    {
        var (code, _, err) = Invoke("frobnicate");

        Assert.Equal(2, code);
        Assert.Contains("frobnicate", err);
    }

    [Fact]
    public void HelpExitsWithZero()
    {
        var (code, output, _) = Invoke("--help");

        Assert.Equal(0, code);
        Assert.Contains("pp-lint check", output);
    }

    [Fact]
    public void RulesListsThePhaseOneCatalog()
    {
        var (code, output, _) = Invoke("rules");

        Assert.Equal(0, code);
        Assert.Contains("NM010", output);
        Assert.Contains("FL201", output);
    }

    [Fact]
    public void UnsupportedFormatExitsWithTwo()
    {
        var (code, _, err) = Invoke("check", CreateMsapp(), "--format", "json");

        Assert.Equal(2, code);
        Assert.Contains("Fase 2", err);
    }
}
