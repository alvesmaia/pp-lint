using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Configuration;
using PpLint.Core.Rules;

namespace PpLint.Rules.Naming;

/// <summary>
/// NM012 — tela fora do padrão de nome do preset.
/// </summary>
[Rule("NM012", RuleCategory.Naming, Severity.Warning)]
public sealed class ScreenNamingRule : IRule
{
    public void Check(LintContext ctx)
    {
        var pattern = ctx.Config.Naming.Screen;
        var regex = Compile(pattern, "naming.screen");

        foreach (var app in ctx.Project.Apps)
        {
            foreach (var screen in app.Screens)
            {
                ctx.Evaluated(1);

                if (regex.IsMatch(screen.Name))
                    continue;

                ctx.Report(
                    screen.Location,
                    $"A tela '{screen.Name}' não segue a convenção do preset '{ctx.Config.PresetName}' "
                    + $"({pattern}). O nome da tela aparece em todo Navigate do app.");
            }
        }
    }

    internal static Regex Compile(string pattern, string chave)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new ConfigException($"Expressão regular inválida em '{chave}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// NM013 — componente canvas fora do padrão de nome.
/// </summary>
[Rule("NM013", RuleCategory.Naming, Severity.Warning)]
public sealed class ComponentNamingRule : IRule
{
    /// <summary>
    /// O template que o Studio dá a uma instância de componente. É por ele que
    /// se distingue componente de controle comum.
    /// </summary>
    private const string ComponentTemplate = "component";

    public void Check(LintContext ctx)
    {
        var pattern = ctx.Config.Naming.Component;
        var regex = ScreenNamingRule.Compile(pattern, "naming.component");

        foreach (var app in ctx.Project.Apps)
        {
            foreach (var control in app.AllControls())
            {
                if (!control.TemplateName.Contains(ComponentTemplate, StringComparison.OrdinalIgnoreCase))
                    continue;

                ctx.Evaluated(1);

                if (regex.IsMatch(control.Name))
                    continue;

                ctx.Report(
                    control.Location,
                    $"O componente '{control.Name}' não segue a convenção do preset "
                    + $"'{ctx.Config.PresetName}' ({pattern}). Componente é reusado em várias telas, e "
                    + "o nome é o que revela isso a quem lê a fórmula.");
            }
        }
    }
}

/// <summary>
/// NM030 — fluxo com o nome que o portal sugeriu.
/// </summary>
[Rule("NM030", RuleCategory.Naming, Severity.Warning)]
public sealed class FlowDefaultNameRule : IRule
{
    /// <summary>
    /// Nomes que o Power Automate propõe ao criar um fluxo do zero ou a partir
    /// de modelo. Quem aceitou o padrão não escolheu nada.
    /// </summary>
    private static readonly Regex DefaultName = new(
        @"^(?:Fluxo|Flow|Untitled|Sem t[íi]tulo|My flow|Meu fluxo)"
        + @"(?:\s*(?:sem t[íi]tulo|\d+))?$"
        + @"|^.*\s+\(\d+\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            ctx.Evaluated(1);

            if (!DefaultName.IsMatch(flow.Name))
                continue;

            ctx.Report(
                flow.Location,
                $"O fluxo '{flow.Name}' ficou com o nome que o portal sugeriu. Num ambiente com dezenas "
                + "de fluxos, é por esse nome que alguém decide qual investigar quando algo falha.");
        }
    }
}

/// <summary>
/// NM031 — ação de fluxo com o nome que o designer gerou.
/// </summary>
[Rule("NM031", RuleCategory.Naming, Severity.Error)]
public sealed class ActionDefaultNameRule : IRule
{
    /// <summary>
    /// Nome gerado pelo designer: o rótulo da operação com sufixo numérico —
    /// 'Compose_2', 'Apply_to_each_3', 'Condition_2'.
    ///
    /// A operação sem sufixo não conta: 'Compose' sozinho é o nome que o
    /// designer dá à primeira, e num fluxo com uma só ela é aceitável.
    /// </summary>
    private static readonly Regex GeneratedName = new(
        @"^(?:Compose|Condition|Apply_to_each|Switch|Scope|Do_until|Increment_variable"
        + @"|Set_variable|Initialize_variable|Append_to_string_variable|Append_to_array_variable"
        + @"|Compor|Condi[cç][aã]o|Aplicar_a_cada|Escopo)_\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Check(LintContext ctx)
    {
        foreach (var flow in ctx.Project.Flows)
        {
            foreach (var action in flow.AllActions())
            {
                ctx.Evaluated(1);

                if (!GeneratedName.IsMatch(action.Name))
                    continue;

                ctx.Report(
                    action.Location,
                    $"A ação '{action.Name}' ficou com o nome que o designer gerou. O nome aparece em "
                    + "toda expressão que consome a saída dela e no histórico de execuções — "
                    + "'Compose_3' não diz o que aquele passo faz.");
            }
        }
    }
}

/// <summary>
/// NM041 — nome curto demais para dizer o que a coisa é.
/// </summary>
[Rule("NM041", RuleCategory.Naming, Severity.Info)]
public sealed class ShortNameRule : IRule
{
    /// <summary>
    /// Comprimento mínimo depois do prefixo de tipo. 'varX' tem um caractere de
    /// nome, e um caractere não descreve nada.
    /// </summary>
    private const int MinLength = 3;

    /// <summary>
    /// Nomes de uma letra aceitos por convenção: índice de laço e coordenada.
    /// Cobrá-los seria discutir estilo, não clareza.
    /// </summary>
    private static readonly HashSet<string> Accepted =
        new(StringComparer.OrdinalIgnoreCase) { "i", "j", "k", "x", "y", "z", "id", "ok" };

    private static readonly Regex TypePrefix = new(
        @"^(?:var|loc|col|ctx|scr|cmp|btn|lbl|txt|img|gal|ico|chk|tgl|rec|frm)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var control in app.AllControls())
            {
                ctx.Evaluated(1);

                var corpo = TypePrefix.Replace(control.Name, string.Empty);

                if (corpo.Length >= MinLength || Accepted.Contains(corpo) || Accepted.Contains(control.Name))
                    continue;

                ctx.Report(
                    control.Location,
                    $"O nome '{control.Name}' tem menos de {MinLength} caracteres depois do prefixo de "
                    + "tipo. Quem ler a fórmula daqui a seis meses não vai deduzir o que ele guarda.");
            }
        }
    }
}
