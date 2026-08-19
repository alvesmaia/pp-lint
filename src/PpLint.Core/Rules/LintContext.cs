using PpLint.Core.Model;
using PpLint.Core.Suppression;

namespace PpLint.Core.Rules;

public sealed class LintContext
{
    private readonly string _ruleId;
    private readonly RuleCategory _category;
    private readonly Severity _severity;
    private readonly List<Diagnostic> _diagnostics;
    private readonly SuppressionIndex _suppressions;

    internal LintContext(
        string ruleId,
        RuleCategory category,
        Severity severity,
        PowerPlatformProject project,
        PpLintConfig config,
        List<Diagnostic> diagnostics,
        SuppressionIndex suppressions)
    {
        _ruleId = ruleId;
        _category = category;
        _severity = severity;
        _diagnostics = diagnostics;
        _suppressions = suppressions;
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
        if (_suppressions.IsSuppressed(_ruleId, location))
        {
            // O alvo continua contando como violação: suprimir tira o achado do
            // relatório, não o débito da nota. Remover o alvo dos dois lados da
            // fração faria a conformidade SUBIR — (v-1)/(t-1) > v/t sempre que
            // v < t — e bastaria silenciar tudo para exibir 100%.
            ViolationCount++;
            return;
        }

        _diagnostics.Add(new Diagnostic(_ruleId, _category, _severity, message, location));
        ViolationCount++;
    }
}
