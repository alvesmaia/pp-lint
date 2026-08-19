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

                if (!prefixes.TryGetValue(control.TemplateName, out var expected))
                    continue;

                if (DefaultControlNameRule.HasDefaultName(control))
                    continue;

                ctx.Evaluated(1);

                if (!control.Name.StartsWith(expected, StringComparison.Ordinal))
                {
                    ctx.Report(
                        control.Location,
                        $"O controle '{control.Name}' é do tipo '{control.TemplateName}' "
                        + $"e deveria começar com o prefixo '{expected}'.");
                }
            }
        }
    }
}
