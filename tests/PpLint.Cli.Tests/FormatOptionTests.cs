namespace PpLint.Cli.Tests;

/// <summary>
/// O CLI só anuncia o formato que entrega. Esta classe guarda essa promessa
/// dos dois lados: o que existe é aceito, o que não existe é recusado com a
/// lista do que funciona.
/// </summary>
public class FormatOptionTests
{
    [Fact]
    public void MarkdownIsRejectedWithTheListOfWhatWorks()
    {
        // Markdown segue fora. O html saiu desta lista quando o relatório
        // passou a existir de verdade.
        var r = ArgumentParser.Parse(["check", "a.msapp", "--format", "md"]);

        Assert.False(r.IsSuccess);
        Assert.Contains("md", r.Error);
        Assert.Contains("text", r.Error);
        Assert.Contains("json", r.Error);
        Assert.Contains("sarif", r.Error);
        Assert.Contains("html", r.Error);
    }

    [Fact]
    public void AnUnknownFormatIsRejected() =>
        Assert.False(ArgumentParser.Parse(["check", "a.msapp", "--format", "pdf"]).IsSuccess);

    [Fact]
    public void TheFourSupportedFormatsAreAccepted()
    {
        foreach (var formato in new[] { "text", "json", "sarif", "html" })
        {
            var r = ArgumentParser.Parse(["check", "a.msapp", "--format", formato]);

            Assert.True(r.IsSuccess, $"'{formato}' devia ser aceito: {r.Error}");
            Assert.Equal(formato, r.Value!.Format);
        }
    }

    [Fact]
    public void HelpAnnouncesExactlyWhatIsAccepted()
    {
        // Um --help que lista um formato a mais é a dívida que a Fase 2c pagou;
        // este teste impede que ela volte.
        var stdout = new StringWriter();
        Program.Run(["--help"], stdout, new StringWriter());

        var linha = stdout.ToString()
            .Split('\n')
            .First(l => l.Contains("--format"));

        Assert.Contains("text", linha);
        Assert.Contains("json", linha);
        Assert.Contains("sarif", linha);
        Assert.Contains("html", linha);
        Assert.DoesNotContain("md", linha);
    }
}
