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
    /// O texto que o parser reconstrói a partir do nó, em caixa baixa. Espaços
    /// e formatação já somem na reconstrução; a caixa some aqui porque o Power Fx
    /// não diferencia maiúsculas em nomes.
    /// </summary>
    public static string Normalize(TexlNode node) =>
        node.ToString().ToLowerInvariant();

    public static bool AreEquivalent(TexlNode a, TexlNode b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
