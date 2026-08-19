using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL201 — variável inicializada no fluxo e nunca lida. Cada
/// InitializeVariable custa uma ação no limite do fluxo e sugere um estado
/// que ninguém consome. Escrever (SetVariable) não conta como uso.
/// </summary>
[Rule("FL201", RuleCategory.Flow, Severity.Warning)]
public sealed class UnusedFlowVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            var expressions = flow.AllActions().SelectMany(a => a.Expressions).ToList();

            foreach (var variable in flow.Variables)
            {
                ctx.Evaluated(1);

                if (!IsRead(variable.Name, expressions))
                {
                    ctx.Report(
                        variable.Location,
                        $"A variável '{variable.Name}' é inicializada no fluxo '{flow.Name}' e nunca lida. "
                        + "Remova a inicialização ou passe a usar o valor.");
                }
            }
        }
    }

    private static bool IsRead(string name, IReadOnlyList<string> expressions)
    {
        var pattern = $@"variables\(\s*'{Regex.Escape(name)}'\s*\)";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return expressions.Any(regex.IsMatch);
    }
}
