using PpLint.Core;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL240 — fluxo sem descrição. Quem abre a lista do Power Automate meses depois
/// só tem o nome para entender o que a automação faz.
/// </summary>
[Rule("FL240", RuleCategory.Flow, Severity.Info)]
public sealed class FlowWithoutDescriptionRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            ctx.Evaluated(1);

            if (string.IsNullOrWhiteSpace(flow.Description))
            {
                ctx.Report(
                    flow.Location,
                    $"O fluxo '{flow.Name}' não tem descrição. Explique em uma linha o que ele faz "
                    + "e quando dispara.");
            }
        }
    }
}
/// <summary>
/// FL230 — gatilho agendado mais frequente que o limiar. Costuma ser polling
/// onde caberia um gatilho de evento, e cada execução consome cota da licença
/// mesmo quando não há nada a fazer.
/// </summary>
[Rule("FL230", RuleCategory.Flow, Severity.Warning)]
public sealed class FrequentRecurrenceRule : IRule
{
    public void Check(LintContext ctx)
    {
        var limite = ctx.Config.Thresholds.MinRecurrenceMinutes;

        foreach (var flow in ctx.Project.Flows)
        {
            var recorrencia = flow.Trigger?.Recurrence;
            if (recorrencia is null)
                continue;

            ctx.Evaluated(1);

            var minutos = EmMinutos(recorrencia);
            if (minutos is not null && minutos < limite)
            {
                ctx.Report(
                    flow.Trigger!.Location,
                    $"O fluxo '{flow.Name}' roda a cada {recorrencia.Interval} {recorrencia.Frequency} "
                    + $"(menos de {limite} minutos). Considere um gatilho de evento no lugar do agendamento.");
            }
        }
    }

    /// <summary>Intervalo em minutos; null para frequências que não cabem em minutos.</summary>
    private static double? EmMinutos(PpLint.Core.Model.FlowRecurrence recorrencia) =>
        recorrencia.Frequency.ToLowerInvariant() switch
        {
            "second" => recorrencia.Interval / 60.0,
            "minute" => recorrencia.Interval,
            "hour" => recorrencia.Interval * 60.0,
            "day" => recorrencia.Interval * 1440.0,
            "week" => recorrencia.Interval * 10080.0,
            "month" => recorrencia.Interval * 43200.0,
            _ => null,
        };
}
