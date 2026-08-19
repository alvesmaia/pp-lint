using System.Globalization;
using System.Text;
using PpLint.Core;
using PpLint.Core.Scoring;

namespace PpLint.Cli;

public static class TextReporter
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static string Render(
        IReadOnlyList<Diagnostic> diagnostics,
        ComplianceReport compliance,
        TimeSpan elapsed,
        bool useColor,
        bool quiet)
    {
        var sb = new StringBuilder();

        if (!quiet)
            RenderDiagnostics(sb, diagnostics, useColor);

        RenderCompliance(sb, compliance, useColor);
        RenderSummary(sb, diagnostics, elapsed);

        return sb.ToString();
    }

    private static void RenderDiagnostics(StringBuilder sb, IReadOnlyList<Diagnostic> diagnostics, bool color)
    {
        if (diagnostics.Count == 0)
        {
            sb.AppendLine(AnsiColors.Apply("Nenhum achado.", AnsiColors.Green, color));
            sb.AppendLine();
            return;
        }

        foreach (var byArtifact in diagnostics.GroupBy(d => d.Location.ArtifactPath))
        {
            sb.AppendLine(AnsiColors.Apply(byArtifact.Key, AnsiColors.Bold, color));

            foreach (var byEntry in byArtifact.GroupBy(d => d.Location.EntryPath))
            {
                if (!string.IsNullOrEmpty(byEntry.Key))
                    sb.Append("  ").AppendLine(AnsiColors.Apply(byEntry.Key, AnsiColors.Dim, color));

                foreach (var bySymbol in byEntry.GroupBy(d => d.Location.Symbol ?? string.Empty))
                {
                    if (!string.IsNullOrEmpty(bySymbol.Key))
                        sb.Append("    ").AppendLine(bySymbol.Key);

                    foreach (var d in bySymbol)
                    {
                        sb.Append("      ")
                          .Append(AnsiColors.Apply(Label(d.Severity).PadRight(7), ColorOf(d.Severity), color))
                          .Append(' ')
                          .Append(d.RuleId)
                          .Append("  ")
                          .AppendLine(d.Message);
                    }
                }
            }

            sb.AppendLine();
        }
    }

    private static void RenderCompliance(StringBuilder sb, ComplianceReport compliance, bool color)
    {
        sb.Append("  ")
          .Append(AnsiColors.Apply("Conformidade geral: ", AnsiColors.Bold, color))
          .AppendLine(AnsiColors.Apply(Percent(compliance.Overall.Percent), ColorForScore(compliance.Overall.Percent), color));

        foreach (var (category, score) in compliance.ByCategory.OrderBy(p => CategoryName(p.Key), StringComparer.Ordinal))
        {
            var conformes = score.EvaluatedTargets - score.Violations;
            sb.Append("    ")
              .Append(CategoryName(category).PadRight(14))
              .Append(Percent(score.Percent).PadLeft(7))
              .Append("  (")
              .Append(conformes.ToString(Br))
              .Append('/')
              .Append(score.EvaluatedTargets.ToString(Br))
              .AppendLine(" conformes)");
        }

        sb.AppendLine();
    }

    private static void RenderSummary(StringBuilder sb, IReadOnlyList<Diagnostic> diagnostics, TimeSpan elapsed)
    {
        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = diagnostics.Count(d => d.Severity == Severity.Warning);
        var infos = diagnostics.Count(d => d.Severity == Severity.Info);

        sb.Append("  Resumo: ")
          .Append(Plural(errors, "erro", "erros")).Append(", ")
          .Append(Plural(warnings, "aviso", "avisos")).Append(", ")
          .Append(Plural(infos, "informação", "informações"))
          .Append(" em ")
          .Append(elapsed.TotalSeconds.ToString("0.0", Br))
          .AppendLine(" s");

        var top = diagnostics
            .GroupBy(d => d.RuleId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();

        if (top.Count > 0)
            sb.Append("  Principais ocorrências: ").AppendLine(string.Join(", ", top));
    }

    private static string Percent(double value) => value.ToString("0.0", Br) + "%";

    private static string Plural(int count, string singular, string plural) =>
        $"{count.ToString(Br)} {(count == 1 ? singular : plural)}";

    private static string Label(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "info",
    };

    private static string ColorOf(Severity severity) => severity switch
    {
        Severity.Error => AnsiColors.Red,
        Severity.Warning => AnsiColors.Yellow,
        _ => AnsiColors.Blue,
    };

    private static string ColorForScore(double percent) => percent switch
    {
        >= 95 => AnsiColors.Green,
        >= 80 => AnsiColors.Yellow,
        _ => AnsiColors.Red,
    };

    private static string CategoryName(RuleCategory category) => category switch
    {
        RuleCategory.Naming => "Nomenclatura",
        RuleCategory.PowerFx => "Power Fx",
        RuleCategory.Flow => "Fluxos",
        RuleCategory.Duplication => "Duplicação",
        RuleCategory.Performance => "Performance",
        RuleCategory.Security => "Segurança",
        RuleCategory.Solution => "Solução",
        _ => category.ToString(),
    };
}
