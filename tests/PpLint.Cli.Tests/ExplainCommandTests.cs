namespace PpLint.Cli.Tests;

public class ExplainCommandTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr, Directory.GetCurrentDirectory());

        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void ExplainPrintsTheDocument()
    {
        var (code, saida, _) = Run("explain", "NM010");

        Assert.Equal(0, code);
        Assert.Contains("O que pega", saida);
        Assert.Contains("Por que importa", saida);
    }

    [Fact]
    public void ExplainAcceptsLowercaseId()
    {
        var (code, saida, _) = Run("explain", "nm010");

        Assert.Equal(0, code);
        Assert.Contains("O que pega", saida);
    }

    [Fact]
    public void UnknownRuleFailsWithAHelpfulMessage()
    {
        var (code, _, erro) = Run("explain", "XX999");

        Assert.Equal(2, code);
        Assert.Contains("XX999", erro);
        // A mensagem precisa dizer como descobrir os IDs válidos, senão o usuário
        // fica só com "não existe".
        Assert.Contains("pp-lint rules", erro);
    }
}
