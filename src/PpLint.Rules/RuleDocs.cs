using System.Collections.Concurrent;
using System.Reflection;

namespace PpLint.Rules;

/// <summary>A documentação de uma regra, como o usuário a lê.</summary>
public sealed record RuleDoc(string Id, string Title, string Summary, string Markdown);

/// <summary>
/// Os documentos de regra embarcados no binário. São a fonte única: alimentam o
/// 'pp-lint explain', o campo help do SARIF e o arquivo publicado no repositório.
///
/// Embarcar em vez de ler do disco é o que faz o binário único funcionar — quem
/// baixa o executável não baixa uma pasta de markdown junto.
/// </summary>
public static class RuleDocs
{
    private const string Prefix = "PpLint.Rules.Docs.";

    private static readonly Assembly Owner = typeof(RuleDocs).Assembly;

    private static readonly ConcurrentDictionary<string, RuleDoc?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Os IDs que têm documento, em ordem.</summary>
    public static IReadOnlyList<string> AvailableIds() =>
        Owner.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)
                        && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..^3])
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    /// <summary>O documento da regra, ou null se ela não tiver um.</summary>
    public static RuleDoc? Find(string ruleId) =>
        Cache.GetOrAdd(ruleId, Load);

    private static RuleDoc? Load(string ruleId)
    {
        // O nome do recurso respeita a caixa do arquivo; a consulta do usuário não.
        var nome = Owner.GetManifestResourceNames()
            .FirstOrDefault(n => string.Equals(n, Prefix + ruleId + ".md", StringComparison.OrdinalIgnoreCase));

        if (nome is null)
            return null;

        using var stream = Owner.GetManifestResourceStream(nome)!;
        using var reader = new StreamReader(stream);
        var markdown = reader.ReadToEnd();

        var id = nome[Prefix.Length..^3];

        return new RuleDoc(id, ParseTitle(markdown, id), ParseSummary(markdown), markdown);
    }

    /// <summary>
    /// O título vem da primeira linha, no formato "# NM010 — Texto". O ID é
    /// removido: quem pediu 'explain NM010' já sabe o ID.
    /// </summary>
    private static string ParseTitle(string markdown, string id)
    {
        var primeira = markdown
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));

        if (primeira is null)
            return id;

        var texto = primeira[2..].Trim();

        if (texto.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            texto = texto[id.Length..].TrimStart(' ', '—', '-', ':');

        return texto.Trim();
    }

    /// <summary>
    /// O resumo é o primeiro parágrafo de "## O que pega" — uma frase, porque vai
    /// para o shortDescription do SARIF, que os visualizadores mostram numa linha.
    /// </summary>
    private static string ParseSummary(string markdown)
    {
        var linhas = markdown.Replace("\r\n", "\n").Split('\n');
        var dentro = false;
        var paragrafo = new List<string>();

        foreach (var linha in linhas)
        {
            if (linha.StartsWith("## ", StringComparison.Ordinal))
            {
                if (dentro)
                    break;

                dentro = linha.Contains("O que pega", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!dentro)
                continue;

            if (linha.Trim().Length == 0)
            {
                if (paragrafo.Count > 0)
                    break;

                continue;
            }

            paragrafo.Add(linha.Trim());
        }

        return string.Join(" ", paragrafo);
    }
}
