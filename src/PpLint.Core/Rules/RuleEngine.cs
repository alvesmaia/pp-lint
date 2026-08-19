using System.Reflection;
using PpLint.Core.Model;

namespace PpLint.Core.Rules;

public sealed class RuleEngine
{
    private readonly List<(IRule Rule, RuleAttribute Meta)> _rules = [];

    public RuleEngine(IEnumerable<IRule> rules)
    {
        foreach (var rule in rules)
        {
            var meta = rule.GetType().GetCustomAttribute<RuleAttribute>()
                ?? throw new InvalidOperationException(
                    $"A regra {rule.GetType().Name} não declara o atributo [Rule].");
            _rules.Add((rule, meta));
        }
    }

    /// <summary>Descobre por reflexão toda classe concreta com [Rule] nos assemblies informados.</summary>
    public static RuleEngine CreateDefault(params Assembly[] assemblies)
    {
        var rules = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IRule).IsAssignableFrom(t)
                        && t.GetCustomAttribute<RuleAttribute>() is not null
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IRule)Activator.CreateInstance(t)!)
            .ToList();

        return new RuleEngine(rules);
    }

    public LintResult Run(PowerPlatformProject project, PpLintConfig config)
    {
        var diagnostics = new List<Diagnostic>();
        var tallies = new List<RuleTally>();

        foreach (var (rule, meta) in _rules)
        {
            if (config.Ignore.Contains(meta.Id))
                continue;

            var severity = config.SeverityOverrides.TryGetValue(meta.Id, out var overridden)
                ? overridden
                : meta.DefaultSeverity;

            var ctx = new LintContext(meta.Id, meta.Category, severity, project, config, diagnostics);
            var before = diagnostics.Count;

            try
            {
                rule.Check(ctx);
            }
            catch (Exception)
            {
                // Uma regra com defeito não pode invalidar a execução inteira:
                // descartamos o que ela produziu e seguimos para a próxima.
                diagnostics.RemoveRange(before, diagnostics.Count - before);
                continue;
            }

            tallies.Add(new RuleTally(meta.Id, meta.Category, severity, ctx.EvaluatedCount, ctx.ViolationCount));
        }

        var sorted = diagnostics
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.RuleId, StringComparer.Ordinal)
            .ThenBy(d => d.Location.ToString(), StringComparer.Ordinal)
            .ToList();

        return new LintResult(sorted, tallies);
    }
}
