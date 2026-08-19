namespace PpLint.Core;

public sealed record Diagnostic(
    string RuleId,
    RuleCategory Category,
    Severity Severity,
    string Message,
    SourceLocation Location);
