using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Performance;

/// <summary>
/// O que as regras de inicialização compartilham.
/// </summary>
internal static class StartupHelpers
{
    /// <summary>
    /// Funções que vão à rede. Cada uma custa uma ida e volta antes de a
    /// primeira tela aparecer.
    /// </summary>
    public static readonly HashSet<string> NetworkFunctions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Collect", "ClearCollect", "Filter", "LookUp", "Search", "Sort",
            "SortByColumns", "Refresh", "CountRows", "CountIf", "Sum", "First", "FirstN",
        };

    /// <summary>
    /// As fontes que vivem fora do dispositivo. Uma chamada só custa ida e volta
    /// quando toca uma delas: ClearCollect sobre tabela literal ou sobre outra
    /// coleção não espera por nada, e contá-la produziria o conselho
    /// "paralelize" para um OnStart que já é instantâneo.
    /// </summary>
    public static HashSet<string> ExternalSources(CanvasApp app) =>
        app.DataSources
            .Where(d => !d.Kind.Contains("Collection", StringComparison.OrdinalIgnoreCase)
                        && !d.Kind.Contains("Static", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>A chamada toca alguma fonte externa em qualquer argumento?</summary>
    public static bool TouchesExternalSource(CallNode call, HashSet<string> externas)
    {
        if (externas.Count == 0)
            return false;

        return AstWalker.Descendants(call)
            .OfType<FirstNameNode>()
            .Any(n => externas.Contains(n.Ident.Name.Value));
    }

    public static PowerFxProperty? OnStart(CanvasApp app) =>
        app.AppProperties.FirstOrDefault(p =>
            string.Equals(p.Name, "OnStart", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// As chamadas de rede na raiz do OnStart, ignorando as que já estão dentro
    /// de um Concurrent — essas já rodam em paralelo, que é o conselho.
    /// </summary>
    public static List<CallNode> TopLevelNetworkCalls(TexlNode root, HashSet<string> externas)
    {
        var dentroDeConcurrent = AstWalker.Calls(root, "Concurrent")
            .SelectMany(c => AstWalker.Descendants(c))
            .OfType<CallNode>()
            .ToHashSet();

        return AstWalker.Descendants(root)
            .OfType<CallNode>()
            .Where(c => !dentroDeConcurrent.Contains(c))
            .Where(c => AstWalker.FunctionName(c) is { } nome && NetworkFunctions.Contains(nome))
            .Where(c => TouchesExternalSource(c, externas))
            .ToList();
    }
}

/// <summary>
/// PERF402 — chamadas de rede no <c>OnStart</c> que rodam uma depois da outra.
/// <c>Concurrent</c> as dispara juntas, e o tempo passa a ser o da mais lenta em
/// vez da soma de todas.
/// </summary>
[Rule("PERF402", RuleCategory.Performance, Severity.Warning)]
public sealed class SequentialStartupRule : IRule
{
    /// <summary>
    /// A partir de quantas chamadas o paralelismo compensa. Com duas, a
    /// diferença raramente é perceptível e o Concurrent atrapalha a leitura.
    /// </summary>
    private const int MinCalls = 3;

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var onStart = StartupHelpers.OnStart(app);
            if (onStart is null)
                continue;

            var root = PowerFxParser.Parse(onStart.Script).Root;
            if (root is null)
                continue;

            ctx.Evaluated(1);

            var chamadas = StartupHelpers.TopLevelNetworkCalls(root, StartupHelpers.ExternalSources(app));
            if (chamadas.Count < MinCalls)
                continue;

            var nomes = chamadas
                .Select(AstWalker.FunctionName)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4);

            ctx.Report(
                onStart.Location,
                $"O OnStart faz {chamadas.Count} chamadas de rede em sequência ({string.Join(", ", nomes)}). "
                + "Envolva-as em Concurrent: o tempo de abertura passa a ser o da chamada mais lenta, "
                + "em vez da soma de todas.");
        }
    }
}

/// <summary>
/// PERF403 — <c>OnStart</c> acima do orçamento de operações. Tudo que está ali
/// acontece antes de a primeira tela aparecer, com o usuário olhando para o
/// logotipo.
/// </summary>
[Rule("PERF403", RuleCategory.Performance, Severity.Warning)]
public sealed class HeavyStartupRule : IRule
{
    /// <summary>
    /// Quantas chamadas de rede cabem no OnStart antes de a abertura ficar
    /// perceptivelmente lenta. Acima disso, vale carregar sob demanda na tela
    /// que precisa do dado.
    /// </summary>
    private const int Budget = 8;

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var onStart = StartupHelpers.OnStart(app);
            if (onStart is null)
                continue;

            var root = PowerFxParser.Parse(onStart.Script).Root;
            if (root is null)
                continue;

            ctx.Evaluated(1);

            // Aqui as chamadas dentro de Concurrent contam: paralelizar reduz o
            // tempo, mas não o volume de dados que desce antes da primeira tela.
            var externas = StartupHelpers.ExternalSources(app);
            var total = AstWalker.Descendants(root)
                .OfType<CallNode>()
                .Where(c => AstWalker.FunctionName(c) is { } nome
                            && StartupHelpers.NetworkFunctions.Contains(nome))
                .Count(c => StartupHelpers.TouchesExternalSource(c, externas));

            if (total <= Budget)
                continue;

            ctx.Report(
                onStart.Location,
                $"O OnStart faz {total} chamadas de rede, acima do orçamento de {Budget}. "
                + "Tudo isso acontece antes de a primeira tela aparecer — carregue sob demanda o que "
                + "só uma tela usa, e deixe no OnStart apenas o que o app inteiro precisa.");
        }
    }
}
