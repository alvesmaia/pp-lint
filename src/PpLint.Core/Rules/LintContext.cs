using PpLint.Core.Model;

namespace PpLint.Core.Rules;

public sealed class LintContext
{
    private readonly string _ruleId;
    private readonly RuleCategory _category;
    private readonly Severity _severity;
    private readonly List<Diagnostic> _diagnostics;

    internal LintContext(
        string ruleId,
        RuleCategory category,
        Severity severity,
        PowerPlatformProject project,
        PpLintConfig config,
        List<Diagnostic> diagnostics)
    {
        _ruleId = ruleId;
        _category = category;
        _severity = severity;
        _diagnostics = diagnostics;
        Project = project;
        Config = config;
    }

    public PowerPlatformProject Project { get; }

    public PpLintConfig Config { get; }

    internal int EvaluatedCount { get; private set; }

    internal int ViolationCount { get; private set; }

    /// <summary>Declara quantos alvos foram examinados — o denominador do índice.</summary>
    public void Evaluated(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        EvaluatedCount += count;
    }

    public void Report(SourceLocation location, string message)
    {
        _diagnostics.Add(new Diagnostic(_ruleId, _category, _severity, message, location));
        ViolationCount++;
    }
}
