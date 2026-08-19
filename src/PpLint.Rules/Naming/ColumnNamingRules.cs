using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Naming;

/// <summary>
/// O que as regras de coluna precisam saber antes de opinar sobre um nome.
/// </summary>
internal static class ColumnTargets
{
    /// <summary>
    /// As colunas que alguém de fato criou, com a tabela de cada uma.
    ///
    /// Ficam de fora as do esquema padrão — auditoria, estado, proprietário,
    /// chave primária — e as que a plataforma derivou de outra, como o par
    /// '_Base' de toda coluna de moeda. Esta última vem marcada como
    /// customizada mesmo sem ninguém tê-la criado, e cobrá-la seria pedir para
    /// renomear algo que o Dataverse recria.
    /// </summary>
    public static IEnumerable<(DataTable Table, DataColumn Column)> Authored(LintContext ctx)
    {
        foreach (var table in ctx.Project.Tables)
            foreach (var column in table.Columns)
                if (column.IsCustom && column.DerivedFrom is null)
                    yield return (table, column);
    }
}

/// <summary>
/// NM020 — coluna criada com um prefixo de publisher que não é o da solução.
/// </summary>
[Rule("NM020", RuleCategory.Naming, Severity.Error)]
public sealed class ColumnPublisherPrefixRule : IRule
{
    public void Check(LintContext ctx)
    {
        var prefixo = ctx.Project.Solution?.PublisherPrefix;

        // Sem manifesto não há prefixo esperado, e inventar um seria pior que
        // não avaliar: um .msapp isolado não tem publisher nenhum.
        if (string.IsNullOrWhiteSpace(prefixo))
            return;

        foreach (var (table, column) in ColumnTargets.Authored(ctx))
        {
            ctx.Evaluated(1);

            var atual = PrefixOf(column.SchemaName);
            if (atual is null || string.Equals(atual, prefixo, StringComparison.OrdinalIgnoreCase))
                continue;

            ctx.Report(
                table.Location,
                $"A coluna '{column.SchemaName}' de '{table.SchemaName}' usa o prefixo '{atual}', "
                + $"e o publisher da solução é '{prefixo}'. Costuma significar que ela foi criada "
                + "no ambiente com o publisher default, e não pela solução — o que quebra a "
                + "portabilidade entre ambientes.");
        }
    }

    /// <summary>O prefixo de 'gmx_Amount' é 'gmx'; sem underscore, não há prefixo.</summary>
    private static string? PrefixOf(string schemaName)
    {
        var i = schemaName.IndexOf('_');
        return i > 0 ? schemaName[..i] : null;
    }
}

/// <summary>
/// NM021 — nome de esquema da coluna fora de PascalCase depois do prefixo.
/// </summary>
[Rule("NM021", RuleCategory.Naming, Severity.Warning)]
public sealed class ColumnSchemaCaseRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (table, column) in ColumnTargets.Authored(ctx))
        {
            ctx.Evaluated(1);

            var corpo = AfterPrefix(column.SchemaName);
            if (corpo.Length == 0 || char.IsUpper(corpo[0]))
                continue;

            ctx.Report(
                table.Location,
                $"A coluna '{column.SchemaName}' de '{table.SchemaName}' não usa PascalCase depois do "
                + $"prefixo. Escreva '{Suggest(column.SchemaName)}': o nome de esquema é o que aparece "
                + "no código, e a caixa inconsistente obriga a conferir cada referência.");
        }
    }

    private static string AfterPrefix(string schemaName)
    {
        var i = schemaName.IndexOf('_');
        return i >= 0 && i + 1 < schemaName.Length ? schemaName[(i + 1)..] : schemaName;
    }

    private static string Suggest(string schemaName)
    {
        var i = schemaName.IndexOf('_');
        if (i < 0 || i + 1 >= schemaName.Length)
            return schemaName;

        return schemaName[..(i + 1)] + char.ToUpperInvariant(schemaName[i + 1]) + schemaName[(i + 2)..];
    }
}

/// <summary>
/// NM023 — nome de exibição que não corresponde ao nome de esquema.
/// </summary>
[Rule("NM023", RuleCategory.Naming, Severity.Info)]
public sealed class ColumnDisplayNameDriftRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (table, column) in ColumnTargets.Authored(ctx))
        {
            if (string.IsNullOrWhiteSpace(column.DisplayName))
                continue;

            ctx.Evaluated(1);

            var esquema = AfterPrefix(column.SchemaName);
            if (Comparable(column.DisplayName!) == Comparable(esquema))
                continue;

            ctx.Report(
                table.Location,
                $"A coluna '{column.SchemaName}' de '{table.SchemaName}' aparece como "
                + $"'{column.DisplayName}' no formulário. Quem lê o formulário e quem lê a fórmula "
                + "veem nomes diferentes, e ligar um ao outro passa a depender de abrir o esquema.");
        }
    }

    private static string AfterPrefix(string schemaName)
    {
        var i = schemaName.IndexOf('_');
        return i >= 0 && i + 1 < schemaName.Length ? schemaName[(i + 1)..] : schemaName;
    }

    /// <summary>
    /// "Expense Date" e "ExpenseDate" são o mesmo nome escrito para leitores
    /// diferentes; a regra existe para divergência de conteúdo, não de espaçamento.
    /// </summary>
    private static string Comparable(string texto) =>
        new(texto.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
