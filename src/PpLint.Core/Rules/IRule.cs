namespace PpLint.Core.Rules;

/// <summary>
/// Uma verificação. Toda implementação deve declarar [Rule] e chamar
/// ctx.Evaluated para cada alvo examinado, mesmo quando não reporta nada —
/// sem isso o índice de conformidade fica incorreto.
/// </summary>
public interface IRule
{
    void Check(LintContext ctx);
}
