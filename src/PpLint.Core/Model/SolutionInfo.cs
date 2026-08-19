namespace PpLint.Core.Model;

public sealed record SolutionInfo(
    string UniqueName,
    string PublisherPrefix,
    string Version,
    bool Managed);
