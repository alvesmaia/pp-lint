namespace PpLint.Core.Suppression;

/// <summary>
/// Uma diretiva `pp-lint: disable=...` encontrada no artefato.
/// <paramref name="Scope"/> é o símbolo onde ela apareceu — a propriedade
/// (btnOk.OnSelect) ou a ação de fluxo (Inicializar).
/// </summary>
public sealed record SuppressionDirective(
    string EntryPath,
    string Scope,
    IReadOnlySet<string> RuleIds);
