using System.Text.RegularExpressions;
using PpLint.Core.Model;

namespace PpLint.Core.Suppression;

/// <summary>
/// Diretivas de supressão encontradas no artefato. Power Fx aceita comentário,
/// então a diretiva vive na própria fórmula; JSON de fluxo não aceita, então
/// vive no campo description da ação.
/// </summary>
public sealed class SuppressionIndex
{
    private static readonly Regex DirectivePattern = new(
        @"pp-lint\s*:\s*disable\s*=\s*(?<ids>[A-Za-z]{2,4}[0-9]{3}(\s*,\s*[A-Za-z]{2,4}[0-9]{3})*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly List<SuppressionDirective> _directives;

    private SuppressionIndex(List<SuppressionDirective> directives) => _directives = directives;

    public static SuppressionIndex Empty { get; } = new([]);

    public IReadOnlyList<SuppressionDirective> Directives => _directives;

    public static SuppressionIndex Build(PowerPlatformProject project)
    {
        var directives = new List<SuppressionDirective>();

        foreach (var app in project.Apps)
        {
            foreach (var property in app.AppProperties)
                AddFrom(directives, property.Script, property.Location.EntryPath, property.Location.Symbol);

            foreach (var control in app.AllControls())
                foreach (var property in control.Properties)
                    AddFrom(directives, property.Script, property.Location.EntryPath, property.Location.Symbol);
        }

        foreach (var flow in project.Flows)
            foreach (var action in flow.AllActions())
                AddFrom(directives, action.Description, action.Location.EntryPath, action.Location.Symbol);

        return new SuppressionIndex(directives);
    }

    public bool IsSuppressed(string ruleId, SourceLocation location)
    {
        foreach (var directive in _directives)
        {
            if (!string.Equals(directive.EntryPath, location.EntryPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!directive.RuleIds.Contains(ruleId))
                continue;

            if (CoversSymbol(directive.Scope, location.Symbol))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A diretiva vale para o símbolo onde está e para o seu dono: uma diretiva
    /// em btnOk.OnSelect silencia também os achados reportados em btnOk, já que
    /// regras de nomenclatura apontam para o controle e não para a propriedade.
    /// </summary>
    private static bool CoversSymbol(string scope, string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return false;

        if (string.Equals(scope, symbol, StringComparison.OrdinalIgnoreCase))
            return true;

        var dot = scope.IndexOf('.');
        return dot > 0 && string.Equals(scope[..dot], symbol, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddFrom(List<SuppressionDirective> directives, string? text, string entryPath, string? scope)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(scope))
            return;

        foreach (Match match in DirectivePattern.Matches(text))
        {
            var ids = match.Groups["ids"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (ids.Count > 0)
                directives.Add(new SuppressionDirective(entryPath, scope, ids));
        }
    }
}
