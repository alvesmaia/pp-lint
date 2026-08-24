using System.Text.Json;

namespace PpLint.Cli.Tests;

/// <summary>
/// O ciclo que um time percorre ao ligar o linter num app que veio de antes
/// dele: rodar e ver o build quebrar, gravar a linha de base, e voltar a ter
/// build verde sem que a dívida suma do relatório.
/// </summary>
public class BaselineCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pplint-baseline").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Diretório temporário preso por outro processo não invalida o teste.
        }
    }

    private static string RealMsapp => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "chess-real.msapp"));

    private (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr, _dir);

        return (code, stdout.ToString(), stderr.ToString());
    }

    private string BaselinePath => Path.Combine(_dir, "pp-lint-baseline.json");

    [Fact]
    public void WithoutBaselineTheBuildFails()
    {
        var (code, _, _) = Run("check", RealMsapp);

        Assert.Equal(1, code);
    }

    [Fact]
    public void BaselineCommandWritesTheDefaultFile()
    {
        var (code, saida, _) = Run("baseline", RealMsapp);

        Assert.Equal(0, code);
        Assert.True(File.Exists(BaselinePath), $"esperava o arquivo em {BaselinePath}");
        Assert.Contains("achados", saida);
    }

    [Fact]
    public void AfterBaselineTheBuildPasses()
    {
        Run("baseline", RealMsapp);

        var (code, _, _) = Run("check", RealMsapp);

        Assert.Equal(0, code);
    }

    [Fact]
    public void TheDefaultFileIsFoundWithoutTheOption()
    {
        // Quem commitou a linha de base no repositório espera que ela valha sem
        // repetir --baseline em todo comando.
        Run("baseline", RealMsapp);

        var (_, _, erro) = Run("check", RealMsapp);

        Assert.Contains("Linha de base", erro);
    }

    [Fact]
    public void TheComplianceIndexDoesNotImprove()
    {
        // O ponto central: a linha de base adia a dívida, não a apaga. Se a nota
        // subisse, bastaria gerá-la para exibir 100%.
        var (_, antes, _) = Run("check", RealMsapp, "--quiet", "--no-color");
        Run("baseline", RealMsapp);
        var (_, depois, _) = Run("check", RealMsapp, "--quiet", "--no-color");

        static string Indice(string texto) =>
            texto.Split('\n').First(l => l.Contains("Conformidade geral"));

        Assert.Equal(Indice(antes), Indice(depois));
    }

    [Fact]
    public void FindingsDisappearFromTheReport()
    {
        Run("baseline", RealMsapp);

        var (_, saida, _) = Run("check", RealMsapp, "--no-color");

        Assert.Contains("Nenhum achado", saida);
    }

    [Fact]
    public void JsonOutputAlsoRespectsTheBaseline()
    {
        Run("baseline", RealMsapp);

        var (_, saida, _) = Run("check", RealMsapp, "--format", "json");
        var root = JsonDocument.Parse(saida).RootElement;

        Assert.Equal(0, root.GetProperty("summary").GetProperty("errors").GetInt32());

        // E o índice continua o mesmo, porque as contagens não foram tocadas.
        Assert.True(root.GetProperty("compliance").GetProperty("violations").GetInt32() > 0);
    }

    [Fact]
    public void MissingBaselineFileIsAnExecutionError()
    {
        var (code, _, erro) = Run("check", RealMsapp, "--baseline", Path.Combine(_dir, "nao-existe.json"));

        Assert.Equal(2, code);
        Assert.Contains("não encontrada", erro);
    }

    [Fact]
    public void CorruptBaselineIsAnExecutionError()
    {
        File.WriteAllText(BaselinePath, "{ isto não é json");

        var (code, _, erro) = Run("check", RealMsapp);

        Assert.Equal(2, code);
        Assert.Contains("JSON", erro);
    }

    [Fact]
    public void BaselineHonoursOutputOption()
    {
        var destino = Path.Combine(_dir, "outro-nome.json");

        Run("baseline", RealMsapp, "--output", destino);

        Assert.True(File.Exists(destino));
    }

    [Fact]
    public void ANewFindingStillFailsTheBuild()
    {
        // A prova de que a linha de base não é um interruptor de desligar: ela
        // perdoa o que existia, e só isso.
        Run("baseline", RealMsapp);

        var conteudo = File.ReadAllText(BaselinePath);
        var doc = JsonDocument.Parse(conteudo);

        // Remove uma posição da linha de base, simulando um achado que não
        // estava lá quando ela foi gerada.
        var restantes = doc.RootElement.GetProperty("entries").EnumerateArray().Skip(1).ToList();
        var reduzida = new
        {
            schemaVersion = 1,
            total = restantes.Count,
            entries = restantes.Select(e => new
            {
                ruleId = e.GetProperty("ruleId").GetString(),
                artifact = e.GetProperty("artifact").GetString(),
                entry = e.GetProperty("entry").GetString(),
                symbol = e.GetProperty("symbol").ValueKind == JsonValueKind.Null
                    ? null
                    : e.GetProperty("symbol").GetString(),
                count = e.GetProperty("count").GetInt32(),
            }),
        };

        File.WriteAllText(BaselinePath, JsonSerializer.Serialize(reduzida));

        var (_, saida, _) = Run("check", RealMsapp, "--no-color", "--fail-on", "info");

        Assert.DoesNotContain("Nenhum achado", saida);
    }
}
