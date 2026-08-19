using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Naming;

/// <summary>
/// NM011 — o nome do controle deve começar com o prefixo do seu tipo
/// (btnSalvar, lblTitulo, galPedidos), de modo que qualquer fórmula revele
/// o tipo do que está sendo referenciado.
/// Telas ficam a cargo de NM012; controles com nome padrão, de NM010.
/// </summary>
[Rule("NM011", RuleCategory.Naming, Severity.Warning)]
public sealed class ControlPrefixRule : IRule
{
    public void Check(LintContext ctx)
    {
        var prefixes = ctx.Config.Naming.ControlPrefixes;

        foreach (var app in ctx.Project.Apps)
        {
            foreach (var control in app.AllControls())
            {
                if (control.IsScreen)
                    continue;

                if (ctx.Config.Naming.GeneratedControlTemplates.Contains(control.TemplateName))
                    continue;

                if (!prefixes.TryGetValue(control.TemplateName, out var expected))
                    continue;

                if (DefaultControlNameRule.HasDefaultName(control))
                    continue;

                ctx.Evaluated(1);

                if (control.Name.StartsWith(expected, StringComparison.Ordinal))
                    continue;

                var misleading = MisleadingPrefix(control.Name, prefixes, expected);

                ctx.Report(
                    control.Location,
                    misleading is null
                        ? $"O controle '{control.Name}' é do tipo '{control.TemplateName}' "
                          + $"e deveria começar com o prefixo '{expected}'."
                        : $"O controle '{control.Name}' usa o prefixo '{misleading.Value.Prefix}', que sugere "
                          + $"'{misleading.Value.Template}', mas o controle é do tipo '{control.TemplateName}'. "
                          + $"Use o prefixo '{expected}'.");
            }
        }
    }

    /// <summary>
    /// Um prefixo que pertence a outro tipo conhecido mente sobre o controle:
    /// quem lê a fórmula depois acredita no nome. Costuma ser copiar-colar de um
    /// controle seguido de troca de tipo, sem renomear — vale uma mensagem própria.
    /// </summary>
    private static (string Prefix, string Template)? MisleadingPrefix(
        string name,
        IReadOnlyDictionary<string, string> prefixes,
        string expected)
    {
        // Ordenado para que um prefixo compartilhado por vários tipos
        // (lbl serve a label e textcanvas) produza sempre a mesma mensagem.
        foreach (var (template, prefix) in prefixes.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (string.Equals(prefix, expected, StringComparison.Ordinal))
                continue;

            if (StartsWithCamelPrefix(name, prefix))
                return (prefix, template);
        }

        return null;
    }

    /// <summary>
    /// O prefixo só conta quando termina de fato: 'img' em "imgLogo" é prefixo,
    /// em "imgsDoProduto" é apenas o começo de outra palavra.
    /// </summary>
    private static bool StartsWithCamelPrefix(string name, string prefix)
    {
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        if (name.Length == prefix.Length)
            return true;

        var next = name[prefix.Length];
        return char.IsUpper(next) || char.IsAsciiDigit(next);
    }
}
