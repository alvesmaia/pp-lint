using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Naming;

/// <summary>
/// NM010 — controle mantido com o nome que o Power Apps Studio gerou
/// (Button1, Label12, Screen1). Nome default torna toda fórmula que o
/// referencia ilegível e é o sintoma mais barato de detectar de um app
/// construído sem convenção.
/// </summary>
[Rule("NM010", RuleCategory.Naming, Severity.Error)]
public sealed class DefaultControlNameRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var control in app.AllControls())
            {
                ctx.Evaluated(1);

                if (HasDefaultName(control))
                {
                    ctx.Report(
                        control.Location,
                        $"O controle '{control.Name}' mantém o nome padrão gerado pelo Studio. "
                        + "Renomeie usando o prefixo do tipo do controle.");
                }
            }
        }
    }

    /// <summary>Nome default = nome do template seguido apenas de dígitos.</summary>
    internal static bool HasDefaultName(Control control)
    {
        var template = control.TemplateName;
        if (string.IsNullOrEmpty(template))
            return false;

        var name = control.Name;
        if (name.Length <= template.Length)
            return false;

        if (!name.StartsWith(template, StringComparison.OrdinalIgnoreCase))
            return false;

        for (var i = template.Length; i < name.Length; i++)
        {
            if (!char.IsAsciiDigit(name[i]))
                return false;
        }

        return true;
    }
}
