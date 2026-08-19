using PpLint.Core.Rules;

namespace PpLint.Core.Scoring;

public sealed record ComplianceScore(double Percent, int EvaluatedTargets, int Violations);

public sealed record ComplianceReport(
    ComplianceScore Overall,
    IReadOnlyDictionary<RuleCategory, ComplianceScore> ByCategory);

/// <summary>
/// Índice de conformidade: proporção de checagens aprovadas sobre checagens
/// realizadas, ponderada por severidade. Cada regra contribui com o número de
/// alvos que examinou (denominador) e de violações que encontrou (numerador).
/// </summary>
public static class ComplianceScorer
{
    public static ComplianceReport Compute(IReadOnlyList<RuleTally> tallies)
    {
        var byCategory = tallies
            .GroupBy(t => t.Category)
            .Select(g => (Category: g.Key, Score: ScoreOf(g)))
            .Where(x => x.Score.EvaluatedTargets > 0)
            .ToDictionary(x => x.Category, x => x.Score);

        return new ComplianceReport(ScoreOf(tallies), byCategory);
    }

    private static ComplianceScore ScoreOf(IEnumerable<RuleTally> tallies)
    {
        double weightedViolations = 0;
        double weightedTargets = 0;
        var targets = 0;
        var violations = 0;

        foreach (var t in tallies)
        {
            var weight = SeverityWeights.Of(t.Severity);
            weightedViolations += weight * t.Violations;
            weightedTargets += weight * t.Evaluated;
            targets += t.Evaluated;
            violations += t.Violations;
        }

        // Sem alvos avaliados não há como estar não-conforme.
        var percent = weightedTargets <= 0
            ? 100.0
            : 100.0 * (1.0 - weightedViolations / weightedTargets);

        return new ComplianceScore(Math.Clamp(percent, 0.0, 100.0), targets, violations);
    }
}
