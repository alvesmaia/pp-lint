using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Naming;

/// <summary>
/// NM014 — propriedade customizada de componente fora de PascalCase.
///
/// A propriedade fica ao lado das embutidas na fórmula que usa o componente —
/// <c>cmpCabecalho.Titulo</c>, ao lado de <c>cmpCabecalho.Fill</c>. Quem lê não
/// tem como saber qual é qual, e é por isso que a convenção é a mesma: começa
/// com maiúscula, sem prefixo de tipo.
/// </summary>
[Rule("NM014", RuleCategory.Naming, Severity.Info)]
public sealed class ComponentPropertyNamingRule : IRule
{
    private static readonly Regex PascalCase = new(
        @"^[A-Z][A-Za-z0-9]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var componente in app.Components)
            {
                foreach (var propriedade in componente.CustomProperties)
                {
                    ctx.Evaluated(1);

                    if (PascalCase.IsMatch(propriedade.Name))
                        continue;

                    ctx.Report(
                        componente.Location,
                        $"A propriedade '{propriedade.Name}' de '{componente.Name}' não usa PascalCase. "
                        + "Ela aparece na fórmula ao lado das embutidas — Fill, Width, OnSelect — e "
                        + "quem lê não tem como saber qual é qual quando a convenção difere.");
                }
            }
        }
    }
}
