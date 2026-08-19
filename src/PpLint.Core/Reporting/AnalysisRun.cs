using PpLint.Core.Rules;
using PpLint.Core.Scoring;

namespace PpLint.Core.Reporting;

/// <summary>O que a análise encontrou num artefato, com o índice dele.</summary>
public sealed record ArtifactAnalysis(
    string Path,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<RuleTally> Tallies,
    ComplianceReport Compliance);

/// <summary>
/// Uma execução inteira do linter: um resultado por artefato analisado, mais o
/// agregado.
///
/// O agregado é calculado sobre todos os tallies juntos, e não pela média dos
/// artefatos. Artefatos têm tamanhos muito diferentes — uma solução com três
/// telas e outra com trezentas — e a média das porcentagens daria à pequena o
/// mesmo peso da grande.
/// </summary>
public sealed record AnalysisRun(
    IReadOnlyList<ArtifactAnalysis> Artifacts,
    ComplianceReport Compliance,
    TimeSpan Elapsed)
{
    /// <summary>Todos os achados, na ordem em que os artefatos foram analisados.</summary>
    public IReadOnlyList<Diagnostic> AllDiagnostics { get; } =
        Artifacts.SelectMany(a => a.Diagnostics).ToList();

    public IReadOnlyList<RuleTally> AllTallies { get; } =
        Artifacts.SelectMany(a => a.Tallies).ToList();

    public static AnalysisRun From(
        IEnumerable<(string Path, LintResult Result)> results, TimeSpan elapsed)
    {
        var artifacts = results
            .Select(r => new ArtifactAnalysis(
                r.Path,
                r.Result.Diagnostics,
                r.Result.Tallies,
                ComplianceScorer.Compute(r.Result.Tallies)))
            .ToList();

        var todos = artifacts.SelectMany(a => a.Tallies).ToList();

        return new AnalysisRun(artifacts, ComplianceScorer.Compute(todos), elapsed);
    }
}
