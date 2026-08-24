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

    /// <summary>
    /// A mesma execução mostrando só os achados indicados, com as contagens
    /// intactas.
    ///
    /// É o que a linha de base precisa: esconder do relatório o achado antigo
    /// sem tirá-lo do índice. Recalcular a conformidade sobre o que sobrou faria
    /// a nota subir por decreto — bastaria gerar uma linha de base para exibir
    /// 100%.
    /// </summary>
    public AnalysisRun ShowingOnly(IReadOnlyList<Diagnostic> visiveis)
    {
        var conjunto = visiveis.ToHashSet();

        var filtrados = Artifacts
            .Select(a => a with { Diagnostics = a.Diagnostics.Where(conjunto.Contains).ToList() })
            .ToList();

        return new AnalysisRun(filtrados, Compliance, Elapsed);
    }

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
