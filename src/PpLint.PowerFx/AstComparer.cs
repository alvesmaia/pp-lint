using System.Globalization;
using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx;

/// <summary>
/// Duas expressões são a mesma coisa? Usado para achar ramos idênticos e
/// condições repetidas, e será a base da detecção de duplicação.
///
/// A comparação é estrutural, não algébrica: 'a - b' e 'b - a' são diferentes,
/// ainda que um humano veja simetria. Provar equivalência algébrica exigiria
/// um provador, e um linter que erra nisso é pior que um que não tenta.
/// </summary>
public static class AstComparer
{
    /// <summary>
    /// O texto que o parser reconstrói, sempre na sintaxe invariante — vírgula
    /// separando argumentos e ponto decimal, como o .msapp guarda. Sem forçar a
    /// cultura, numa máquina pt-BR a expressão sairia com ';' e ',', e a sugestão
    /// exibida ao usuário não poderia ser colada de volta na fórmula.
    /// </summary>
    public static string Render(TexlNode node)
    {
        var anterior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return node.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    /// <summary>
    /// A forma canônica usada na comparação. Preserva a caixa: em Power Fx,
    /// "Sim" e "sim" são textos diferentes, e "mm" e "MM" são minuto e mês.
    /// Achatar tudo faria a PF113 acusar ramos que retornam coisas distintas.
    ///
    /// O custo é não reconhecer 'varTotal' e 'VARTOTAL' como o mesmo nome — um
    /// falso negativo raro, que é o lado certo para errar numa regra de erro.
    /// </summary>
    public static string Normalize(TexlNode node) => Render(node);

    /// <summary>
    /// A citação de um trecho dentro de uma mensagem: uma linha só e curta o
    /// bastante para caber no terminal. Fórmulas reais têm dezenas de linhas, e
    /// despejá-las inteiras quebra o alinhamento do relatório e esconde o
    /// conselho que vem depois. O texto integral continua em Render.
    /// </summary>
    public static string Quote(TexlNode node, int limite = 70)
    {
        var uma_linha = string.Join(" ", Render(node).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return uma_linha.Length <= limite ? uma_linha : uma_linha[..(limite - 1)] + "…";
    }

    public static bool AreEquivalent(TexlNode a, TexlNode b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
