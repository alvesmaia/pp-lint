using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Solution;

/// <summary>
/// SOL601 — tabela na solução sem o prefixo do publisher.
/// </summary>
[Rule("SOL601", RuleCategory.Solution, Severity.Warning)]
public sealed class TablePublisherPrefixRule : IRule
{
    public void Check(LintContext ctx)
    {
        var prefixo = ctx.Project.Solution?.PublisherPrefix;
        if (string.IsNullOrWhiteSpace(prefixo))
            return;

        foreach (var table in ctx.Project.Tables)
        {
            ctx.Evaluated(1);

            var atual = PrefixOf(table.SchemaName);

            // Sem prefixo nenhum é tabela do sistema — account, contact e as
            // demais que já vêm no ambiente. Elas não pertencem a publisher
            // nenhum e não podem ser cobradas.
            if (atual is null || string.Equals(atual, prefixo, StringComparison.OrdinalIgnoreCase))
                continue;

            ctx.Report(
                table.Location,
                $"A tabela '{table.SchemaName}' usa o prefixo '{atual}', e o publisher da solução é "
                + $"'{prefixo}'. Ela pertence a outro publisher: ao promover a solução, quem importa "
                + "precisa ter aquele publisher instalado, e a dependência não é óbvia no pacote.");
        }
    }

    private static string? PrefixOf(string schemaName)
    {
        var i = schemaName.IndexOf('_');
        return i > 0 ? schemaName[..i] : null;
    }
}

/// <summary>
/// SOL602 — fórmula ou expressão que cita coluna inexistente numa tabela que a
/// própria solução declara.
///
/// A regra só fala sobre tabelas extraídas do artefato. Sobre fonte que ela não
/// enxerga — uma lista do SharePoint fora da solução — ficar calada é a única
/// resposta honesta.
/// </summary>
[Rule("SOL602", RuleCategory.Solution, Severity.Error)]
public sealed class UnknownColumnRule : IRule
{
    public void Check(LintContext ctx)
    {
        if (ctx.Project.Tables.Count == 0)
            return;

        var colunasPorTabela = ctx.Project.Tables.ToDictionary(
            t => t.LogicalName,
            t => t.Columns
                .SelectMany(c => new[] { c.LogicalName, c.SchemaName })
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var flow in ctx.Project.Flows)
        {
            foreach (var action in flow.AllActions())
            {
                foreach (var expressao in action.Expressions)
                {
                    foreach (var (tabela, coluna) in ColumnReferences(expressao))
                    {
                        if (!colunasPorTabela.TryGetValue(tabela, out var conhecidas))
                            continue;

                        ctx.Evaluated(1);

                        if (conhecidas.Contains(coluna))
                            continue;

                        ctx.Report(
                            action.Location,
                            $"'{action.Name}' cita a coluna '{coluna}' de '{tabela}', que não existe no "
                            + "esquema desta solução. A expressão devolve nulo em execução, sem falhar — "
                            + "confira o nome ou inclua a coluna na solução.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Referências no formato 'tabela/coluna' e 'tabela'?['coluna'], que é como
    /// as expressões de fluxo citam campo do Dataverse.
    /// </summary>
    private static IEnumerable<(string Tabela, string Coluna)> ColumnReferences(string expressao)
    {
        var padrao = new System.Text.RegularExpressions.Regex(
            @"'?([A-Za-z][A-Za-z0-9_]*)'?\?\['([A-Za-z][A-Za-z0-9_]*)'\]",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        foreach (System.Text.RegularExpressions.Match m in padrao.Matches(expressao))
            yield return (m.Groups[1].Value, m.Groups[2].Value);
    }
}

/// <summary>
/// SOL603 — solução managed sendo analisada.
/// </summary>
[Rule("SOL603", RuleCategory.Solution, Severity.Info)]
public sealed class ManagedSolutionRule : IRule
{
    public void Check(LintContext ctx)
    {
        if (ctx.Project.Solution is not { } solution)
            return;

        ctx.Evaluated(1);

        if (!solution.Managed)
            return;

        ctx.Report(
            SolutionLocation(ctx),
            $"'{solution.UniqueName}' é uma solução managed, que é o pacote de distribuição e não a "
            + "fonte. Os achados abaixo apontam para código que você não pode editar aqui — analise a "
            + "solução unmanaged do ambiente de desenvolvimento.");
    }

    private static SourceLocation SolutionLocation(LintContext ctx) =>
        new(ctx.Project.SourcePath, "solution.xml", ctx.Project.Solution!.UniqueName, 0, 0);
}
