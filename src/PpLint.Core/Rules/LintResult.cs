namespace PpLint.Core.Rules;

public sealed record RuleTally(
    string RuleId,
    RuleCategory Category,
    Severity Severity,
    int Evaluated,
    int Violations);

public sealed record LintResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<RuleTally> Tallies);
