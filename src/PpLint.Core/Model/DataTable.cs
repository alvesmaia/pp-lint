namespace PpLint.Core.Model;

/// <summary>
/// Uma coluna de tabela do Dataverse ou de lista do SharePoint.
///
/// <paramref name="IsCustom"/> e <paramref name="DerivedFrom"/> existem para
/// separar o que alguém escreveu do que a plataforma gerou. Sem os dois, as
/// regras de nomenclatura acusariam as colunas de auditoria, as de estado e o
/// par de moeda — nenhuma delas escolhida por ninguém.
/// </summary>
public sealed record DataColumn(
    string LogicalName,
    string SchemaName,
    string Type,
    bool Required)
{
    /// <summary>O nome que aparece no formulário, que pode e deve ser legível.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Coluna criada por alguém, e não parte do esquema padrão da tabela.</summary>
    public bool IsCustom { get; init; }

    /// <summary>
    /// A coluna da qual esta deriva, quando a plataforma a gerou como cálculo de
    /// outra — é o caso do par '_Base' de toda coluna de moeda, que vem marcado
    /// como customizado mesmo sem ninguém o ter criado.
    /// </summary>
    public string? DerivedFrom { get; init; }
}

public sealed record DataTable(
    string LogicalName,
    string SchemaName,
    IReadOnlyList<DataColumn> Columns,
    SourceLocation Location);
