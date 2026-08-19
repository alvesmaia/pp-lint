namespace PpLint.Core.Rules;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuleAttribute(string id, RuleCategory category, Severity defaultSeverity) : Attribute
{
    public string Id { get; } = id;
    public RuleCategory Category { get; } = category;
    public Severity DefaultSeverity { get; } = defaultSeverity;
}
