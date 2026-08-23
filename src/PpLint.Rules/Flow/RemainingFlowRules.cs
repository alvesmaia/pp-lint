using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Flow;

/// <summary>
/// FL211 — o fluxo trata falha, mas não relata: o ramo de erro corre e ninguém
/// fica sabendo.
/// </summary>
[Rule("FL211", RuleCategory.Flow, Severity.Warning)]
public sealed class SilentFailureHandlingRule : IRule
{
    /// <summary>
    /// Tipos de ação que avisam alguém ou deixam rastro. Uma variável definida
    /// no ramo de erro não avisa ninguém.
    /// </summary>
    private static readonly Regex Reporting = new(
        @"OpenApiConnection|Http|ApiConnection|SendEmail|Terminate|Compose$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            var acoes = flow.AllActions().ToList();

            var deErro = acoes
                .Where(a => a.RunAfterStates.Any(e =>
                    !string.Equals(e, "Succeeded", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (deErro.Count == 0)
                continue;

            ctx.Evaluated(1);

            if (deErro.Any(a => Reporting.IsMatch(a.Type) || a.Type.Equals("Terminate", StringComparison.OrdinalIgnoreCase)))
                continue;

            ctx.Report(
                flow.Location,
                $"O fluxo '{flow.Name}' tem tratamento de falha, mas nenhuma ação do ramo de erro avisa "
                + "alguém nem encerra o fluxo com estado de falha. A falha é engolida em silêncio, e o "
                + "histórico de execuções mostra sucesso.");
        }
    }
}

/// <summary>
/// FL223 — <c>Aplicar a cada</c> com concorrência desligada. O padrão é
/// sequencial: um item de cada vez, mesmo quando a ordem não importa.
/// </summary>
[Rule("FL223", RuleCategory.Flow, Severity.Info)]
public sealed class SequentialLoopRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            foreach (var action in flow.AllActions().Where(a => IsLoop(a.Type)))
            {
                ctx.Evaluated(1);

                if (action.ConcurrencyDegree is > 1)
                    continue;

                ctx.Report(
                    action.Location,
                    $"O laço '{action.Name}' roda um item de cada vez, que é o padrão. Se a ordem dos "
                    + "itens não importa e um não depende do anterior, ligar a concorrência nas "
                    + "configurações da ação reduz o tempo total.");
            }
        }
    }

    private static bool IsLoop(string type) =>
        type.Equals("Foreach", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// FL231 — gatilho sem condição, com o fluxo saindo por uma condição logo na
/// primeira ação.
/// </summary>
[Rule("FL231", RuleCategory.Flow, Severity.Warning)]
public sealed class MissingTriggerConditionRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            if (flow.Trigger is null || flow.TriggerHasCondition)
                continue;

            // Só faz sentido para gatilho de evento: recorrência não tem
            // condição de gatilho para configurar.
            if (flow.Trigger.Recurrence is not null)
                continue;

            ctx.Evaluated(1);

            var primeira = flow.Actions.FirstOrDefault(a => a.RunAfter.Count == 0);
            if (primeira is null || !IsCondition(primeira.Type))
                continue;

            ctx.Report(
                flow.Location,
                $"O gatilho de '{flow.Name}' não tem condição, e a primeira ação é o desvio "
                + $"'{primeira.Name}'. O fluxo acorda a cada evento só para decidir que não fará nada: "
                + "cada execução dessas consome a licença. Mova o teste para a condição de gatilho.");
        }
    }

    private static bool IsCondition(string type) =>
        type.Equals("If", StringComparison.OrdinalIgnoreCase)
        || type.Equals("Switch", StringComparison.OrdinalIgnoreCase);
}
