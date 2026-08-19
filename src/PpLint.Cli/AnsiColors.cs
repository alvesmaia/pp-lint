namespace PpLint.Cli;

/// <summary>Códigos ANSI mínimos — sem dependência externa, seguros para AOT.</summary>
internal static class AnsiColors
{
    private const string Esc = "\u001b";

    public const string Reset = Esc + "[0m";
    public const string Bold = Esc + "[1m";
    public const string Dim = Esc + "[2m";
    public const string Red = Esc + "[31m";
    public const string Green = Esc + "[32m";
    public const string Yellow = Esc + "[33m";
    public const string Blue = Esc + "[34m";

    public static string Apply(string text, string color, bool enabled) =>
        enabled ? color + text + Reset : text;
}
