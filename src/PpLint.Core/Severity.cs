namespace PpLint.Core;

public enum Severity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public static class SeverityWeights
{
    public static int Of(Severity severity) => severity switch
    {
        Severity.Error => 10,
        Severity.Warning => 3,
        Severity.Info => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };
}
