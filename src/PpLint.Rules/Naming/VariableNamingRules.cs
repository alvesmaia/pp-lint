using System.Text.RegularExpressions;
using PpLint.Core;
using PpLint.Core.Configuration;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Naming;

/// <summary>
/// Base das regras que comparam o nome de uma variável com o regex do preset.
/// As três diferem apenas no tipo de variável e em qual padrão consultam.
/// </summary>
public abstract class VariableNamingRuleBase : IRule
{
    protected abstract VariableKind Kind { get; }

    protected abstract string PatternOf(NamingConfig naming);

    /// <summary>Nome da chave no TOML, para que a mensagem de erro diga o que corrigir.</summary>
    protected abstract string ConfigKey { get; }

    protected abstract string Describe { get; }

    public void Check(LintContext ctx)
    {
        var pattern = PatternOf(ctx.Config.Naming);
        Regex regex;

        try
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            // Configuração quebrada precisa chegar ao usuário como erro de
            // execução, não sumir com a regra que deixou de rodar.
            throw new ConfigException(
                $"Expressão regular inválida em '{ConfigKey}': {ex.Message}", ex);
        }

        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);

            foreach (var variable in graph.Definitions.Where(d => d.Kind == Kind))
            {
                ctx.Evaluated(1);

                if (!regex.IsMatch(variable.Name))
                {
                    ctx.Report(
                        variable.Location,
                        $"{Describe} '{variable.Name}' não segue a convenção do preset "
                        + $"'{ctx.Config.PresetName}' ({pattern}). Ajuste o nome ou '{ConfigKey}' no pp-lint.toml.");
                }
            }
        }
    }
}

/// <summary>NM001 — variável global fora da convenção de nomes.</summary>
[Rule("NM001", RuleCategory.Naming, Severity.Warning)]
public sealed class GlobalVariableNamingRule : VariableNamingRuleBase
{
    protected override VariableKind Kind => VariableKind.Global;
    protected override string PatternOf(NamingConfig naming) => naming.GlobalVariable;
    protected override string ConfigKey => "global-variable";
    protected override string Describe => "A variável global";
}

/// <summary>NM002 — variável de contexto fora da convenção de nomes.</summary>
[Rule("NM002", RuleCategory.Naming, Severity.Warning)]
public sealed class ContextVariableNamingRule : VariableNamingRuleBase
{
    protected override VariableKind Kind => VariableKind.Context;
    protected override string PatternOf(NamingConfig naming) => naming.ContextVariable;
    protected override string ConfigKey => "context-variable";
    protected override string Describe => "A variável de contexto";
}

/// <summary>NM003 — coleção fora da convenção de nomes.</summary>
[Rule("NM003", RuleCategory.Naming, Severity.Warning)]
public sealed class CollectionNamingRule : VariableNamingRuleBase
{
    protected override VariableKind Kind => VariableKind.Collection;
    protected override string PatternOf(NamingConfig naming) => naming.Collection;
    protected override string ConfigKey => "collection";
    protected override string Describe => "A coleção";
}
