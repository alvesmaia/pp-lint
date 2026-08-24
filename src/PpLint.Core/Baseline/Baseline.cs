using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PpLint.Core.Reporting;

namespace PpLint.Core.Baseline;

/// <summary>
/// Uma linha de assinatura na linha de base: quantas violações daquela regra
/// existiam naquele ponto do artefato.
///
/// A assinatura não inclui a mensagem. Se incluísse, melhorar o texto de uma
/// regra faria toda a linha de base envelhecer de uma vez, e centenas de
/// achados já revisados voltariam a aparecer como novos.
/// </summary>
public sealed record BaselineEntry(
    string RuleId,
    string Artifact,
    string Entry,
    string? Symbol,
    int Count);

/// <summary>
/// As violações que já existiam quando alguém apontou o linter para um app que
/// vinha de antes dele.
///
/// Serve para adotar o pp-lint sem parar o time: as violações antigas continuam
/// no relatório de conformidade — a nota não melhora por decreto — mas param de
/// quebrar o build. Só o que aparecer depois falha.
/// </summary>
public sealed class Baseline
{
    /// <summary>
    /// Sobe quando o formato mudar de um jeito que impeça ler um arquivo antigo.
    /// Sem isto, uma linha de base gerada por outra versão seria interpretada
    /// errado em silêncio.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>O nome procurado quando ninguém passa caminho.</summary>
    public const string DefaultFileName = "pp-lint-baseline.json";

    private readonly Dictionary<string, int> _restante;

    private Baseline(IReadOnlyList<BaselineEntry> entries)
    {
        Entries = entries;
        _restante = entries.ToDictionary(Key, e => e.Count, StringComparer.Ordinal);
    }

    public IReadOnlyList<BaselineEntry> Entries { get; }

    public int Total => Entries.Sum(e => e.Count);

    public static Baseline Empty { get; } = new([]);

    /// <summary>A linha de base que descreve exatamente o que esta execução achou.</summary>
    public static Baseline From(AnalysisRun run) =>
        new(run.AllDiagnostics
            .GroupBy(Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var d = g.First();
                return new BaselineEntry(d.RuleId, d.Location.ArtifactPath, d.Location.EntryPath, d.Location.Symbol, g.Count());
            })
            .OrderBy(e => e.RuleId, StringComparer.Ordinal)
            .ThenBy(e => e.Artifact, StringComparer.Ordinal)
            .ThenBy(e => e.Entry, StringComparer.Ordinal)
            .ThenBy(e => e.Symbol ?? string.Empty, StringComparer.Ordinal)
            .ToList());

    /// <summary>
    /// Os achados que a linha de base não cobre — o que apareceu depois dela.
    ///
    /// Consome as contagens conforme casa: com três violações registradas e
    /// quatro encontradas, a quarta é nova. Isto é o que impede acrescentar um
    /// quinto problema idêntico ao lado de quatro já perdoados.
    /// </summary>
    public IReadOnlyList<Diagnostic> Unbaselined(IReadOnlyList<Diagnostic> diagnostics)
    {
        if (Entries.Count == 0)
            return diagnostics;

        var saldo = new Dictionary<string, int>(_restante, StringComparer.Ordinal);
        var novos = new List<Diagnostic>();

        foreach (var d in diagnostics)
        {
            var chave = Key(d);

            if (saldo.TryGetValue(chave, out var quantos) && quantos > 0)
            {
                saldo[chave] = quantos - 1;
                continue;
            }

            novos.Add(d);
        }

        return novos;
    }

    public string ToJson()
    {
        using var stream = new MemoryStream();

        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            // O arquivo é lido por humanos em revisão de código: escapar
            // acentuação e apóstrofos o tornaria ilegível.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteNumber("total", Total);

            w.WriteStartArray("entries");
            foreach (var e in Entries)
            {
                w.WriteStartObject();
                w.WriteString("ruleId", e.RuleId);
                w.WriteString("artifact", e.Artifact);
                w.WriteString("entry", e.Entry);
                if (e.Symbol is null)
                    w.WriteNull("symbol");
                else
                    w.WriteString("symbol", e.Symbol);
                w.WriteNumber("count", e.Count);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Baseline Parse(string json)
    {
        JsonDocument doc;

        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new BaselineException($"A linha de base não é um JSON válido: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("schemaVersion", out var versao) || versao.ValueKind != JsonValueKind.Number)
                throw new BaselineException("A linha de base não declara 'schemaVersion'.");

            if (versao.GetInt32() != SchemaVersion)
            {
                throw new BaselineException(
                    $"A linha de base é da versão {versao.GetInt32()} e esta versão do pp-lint lê a "
                    + $"{SchemaVersion}. Gere-a de novo com 'pp-lint baseline'.");
            }

            if (!root.TryGetProperty("entries", out var lista) || lista.ValueKind != JsonValueKind.Array)
                throw new BaselineException("A linha de base não tem a lista 'entries'.");

            var entries = new List<BaselineEntry>();

            foreach (var item in lista.EnumerateArray())
            {
                var ruleId = Text(item, "ruleId");
                if (ruleId is null)
                    continue;

                entries.Add(new BaselineEntry(
                    ruleId,
                    Text(item, "artifact") ?? string.Empty,
                    Text(item, "entry") ?? string.Empty,
                    Text(item, "symbol"),
                    item.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number
                        ? c.GetInt32()
                        : 1));
            }

            return new Baseline(entries);
        }
    }

    private static string? Text(JsonElement element, string nome) =>
        element.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// A assinatura de um achado. O caractere nulo separa os campos para que
    /// um símbolo contendo barra não possa ser confundido com uma entrada.
    /// </summary>
    private static string Key(Diagnostic d) =>
        string.Join('\0', d.RuleId, d.Location.ArtifactPath, d.Location.EntryPath, d.Location.Symbol ?? string.Empty);

    private static string Key(BaselineEntry e) =>
        string.Join('\0', e.RuleId, e.Artifact, e.Entry, e.Symbol ?? string.Empty);
}

/// <summary>Linha de base ilegível ou de outra versão.</summary>
public sealed class BaselineException : Exception
{
    public BaselineException(string message) : base(message) { }

    public BaselineException(string message, Exception inner) : base(message, inner) { }
}
