using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL203 — ação puramente computacional cuja saída ninguém consome. Cada ação
/// conta no limite do fluxo, e uma que não alimenta nada costuma ser resto de
/// depuração.
///
/// Só entram tipos sem efeito colateral: uma chamada de conector pode existir
/// justamente pelo efeito — enviar e-mail, gravar registro — e cobrar uso da
/// saída dela seria errado.
/// </summary>
[Rule("FL203", RuleCategory.Flow, Severity.Warning)]
public sealed class UnusedOutputRule : IRule
{
    private static readonly string[] ComputationalTypes =
        ["Compose", "Select", "Query", "ParseJson", "Join", "Table", "Csv"];

    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            var acoes = flow.AllActions().ToList();
            var todasExpressoes = acoes.SelectMany(a => a.Expressions).ToList();

            foreach (var acao in acoes)
            {
                if (!ComputationalTypes.Contains(acao.Type, StringComparer.OrdinalIgnoreCase))
                    continue;

                ctx.Evaluated(1);

                if (!Consumida(acao.Name, todasExpressoes))
                {
                    ctx.Report(
                        acao.Location,
                        $"A saída da ação '{acao.Name}' não é usada em lugar nenhum do fluxo. "
                        + "Remova a ação ou consuma o resultado.");
                }
            }
        }
    }

    /// <summary>
    /// O Power Automate referencia a saída de uma ação por outputs('Nome'),
    /// body('Nome') ou pelo atalho @Nome.
    /// </summary>
    private static bool Consumida(string nome, IReadOnlyList<string> expressoes)
    {
        var escapado = Regex.Escape(nome);
        var padrao = new Regex(
            $@"(outputs|body|actionOutputs|actionBody)\(\s*'{escapado}'\s*\)|@\{{?\s*{escapado}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return expressoes.Any(padrao.IsMatch);
    }
}
