using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// O que as regras de conector precisam saber sobre as operações que listam
/// registros.
/// </summary>
internal static class QueryOperations
{
    /// <summary>
    /// Operações que trazem uma página de registros e aceitam recorte do lado
    /// do servidor. A lista é curta e nomeada: cada entrada foi conferida no
    /// JSON de um fluxo real, e inventar nome de operação produziria regra que
    /// nunca dispara ou que dispara no lugar errado.
    /// </summary>
    private static readonly HashSet<string> Paged =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "GetItems",       // SharePoint "Obter itens"; Excel "Listar linhas de uma tabela"
            "GetFileItems",   // SharePoint "Obter arquivos (somente propriedades)"
            "ListRecords",    // Dataverse "Listar linhas"
            "ListRecordsWithOrganization",
            "GetRows",        // SQL Server "Obter linhas"
            "GetRows_V2",
            "GetItemsV2",
        };

    public static bool IsPagedQuery(FlowAction action) =>
        action.OperationId is { } id && Paged.Contains(id);

    public static IEnumerable<(CloudFlow Flow, FlowAction Action)> AllQueries(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
            foreach (var action in flow.AllActions())
                if (IsPagedQuery(action))
                    yield return (flow, action);
    }

    public static bool Has(FlowAction action, string parametro) =>
        action.ParameterNames.Contains(parametro, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// FL212 — ação com resultado estático ligado, que é o que o designer chama de
/// desabilitar.
/// </summary>
[Rule("FL212", RuleCategory.Flow, Severity.Warning)]
public sealed class DisabledActionRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            foreach (var action in flow.AllActions())
            {
                ctx.Evaluated(1);

                if (!action.StaticResultEnabled)
                    continue;

                ctx.Report(
                    action.Location,
                    $"A ação '{action.Name}' está com resultado estático ligado: ela devolve saída "
                    + "simulada e não executa. Isso costuma ser um teste que ficou ligado — o fluxo "
                    + "aparece verde no histórico sem ter feito o trabalho.");
            }
        }
    }
}

/// <summary>
/// FL220 — consulta que traz a tabela inteira para filtrar depois.
/// </summary>
[Rule("FL220", RuleCategory.Flow, Severity.Warning)]
public sealed class QueryWithoutFilterRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (_, action) in QueryOperations.AllQueries(ctx))
        {
            ctx.Evaluated(1);

            if (QueryOperations.Has(action, "$filter"))
                continue;

            ctx.Report(
                action.Location,
                $"'{action.Name}' lista registros sem '$filter': a consulta traz tudo que couber na "
                + "página e o recorte acontece depois, dentro do fluxo. Filtrar no servidor reduz o "
                + "tráfego e o tempo, e evita que a resposta certa fique fora da página trazida.");
        }
    }
}

/// <summary>
/// FL221 — consulta sem limite de linhas.
/// </summary>
[Rule("FL221", RuleCategory.Flow, Severity.Info)]
public sealed class QueryWithoutTopRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var (_, action) in QueryOperations.AllQueries(ctx))
        {
            ctx.Evaluated(1);

            if (QueryOperations.Has(action, "$top"))
                continue;

            ctx.Report(
                action.Location,
                $"'{action.Name}' lista registros sem '$top'. Enquanto a tabela é pequena não faz "
                + "diferença; quando crescer, o fluxo passa a percorrer tudo a cada execução sem que "
                + "ninguém tenha mudado nada. Declare quantas linhas o fluxo precisa.");
        }
    }
}

/// <summary>
/// FL224 — chamada de conector dentro de laço.
/// </summary>
[Rule("FL224", RuleCategory.Flow, Severity.Warning)]
public sealed class ConnectorInLoopRule : IRule
{
    /// <summary>
    /// Tipo de ação que atravessa a rede. Uma composição ou uma variável dentro
    /// do laço custa quase nada; uma chamada de conector custa uma requisição
    /// por item.
    /// </summary>
    private static bool IsConnectorCall(FlowAction action) =>
        action.Type.Equals("OpenApiConnection", StringComparison.OrdinalIgnoreCase)
        || action.Type.Equals("ApiConnection", StringComparison.OrdinalIgnoreCase)
        || action.Type.Equals("Http", StringComparison.OrdinalIgnoreCase);

    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
            foreach (var action in flow.Actions)
                Percorrer(ctx, action, dentroDeLaco: false, laco: null);
    }

    private static void Percorrer(LintContext ctx, FlowAction action, bool dentroDeLaco, string? laco)
    {
        if (IsConnectorCall(action))
        {
            ctx.Evaluated(1);

            if (dentroDeLaco)
            {
                ctx.Report(
                    action.Location,
                    $"'{action.Name}' chama um conector dentro do laço '{laco}': é uma requisição por "
                    + "item. Com poucos itens ninguém nota; com algumas centenas, o fluxo estoura o "
                    + "limite de requisições da licença e leva horas. Veja se a operação aceita lote, "
                    + "ou ligue a concorrência do laço.");
            }
        }

        var ehLaco = action.Type.Equals("Foreach", StringComparison.OrdinalIgnoreCase)
                     || action.Type.Equals("Until", StringComparison.OrdinalIgnoreCase);

        foreach (var filho in action.Children)
            Percorrer(ctx, filho, dentroDeLaco || ehLaco, ehLaco ? action.Name : laco);
    }
}
