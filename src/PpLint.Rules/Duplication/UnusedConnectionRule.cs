using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Duplication;

/// <summary>
/// DUP306 — conexão declarada no app sem nenhuma fonte de dados e sem nenhum
/// controle dependendo dela.
///
/// Diferente da DUP305, que olha fonte de dados: uma conexão é a autorização ao
/// conector, e pode existir sozinha — foi criada, o conector foi removido
/// depois, e a autorização ficou.
/// </summary>
[Rule("DUP306", RuleCategory.Duplication, Severity.Warning)]
public sealed class UnusedConnectionRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var conexao in app.Connections)
            {
                ctx.Evaluated(1);

                // O Studio registra em cada conexão quais fontes vêm dela e
                // quais controles dependem dela. Zero dos dois é o sinal.
                if (conexao.DataSourceCount > 0 || conexao.DependentCount > 0)
                    continue;

                ctx.Report(
                    app.Location,
                    $"A conexão '{conexao.DisplayName}' não alimenta fonte de dados nem controle "
                    + "nenhum. Ela continua na lista de dependências da solução: quem faz o deploy "
                    + "precisa autorizar um conector que o app não usa, e o app não abre se essa "
                    + "autorização falhar.");
            }
        }
    }
}
