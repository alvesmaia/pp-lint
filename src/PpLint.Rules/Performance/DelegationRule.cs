using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Performance;

/// <summary>
/// PERF401 — função não delegável aplicada sobre uma fonte de dados externa.
///
/// Quando a expressão não é delegável, o Power Apps traz só os primeiros
/// registros — 500 por padrão, 2000 no máximo — e avalia o resto no
/// dispositivo. O app não dá erro: ele mostra a resposta errada, calculada
/// sobre um pedaço dos dados. Numa tabela que ainda vai crescer, o defeito
/// aparece meses depois, quando alguém repara que o total não bate.
///
/// A regra só olha para fontes externas. Coleção vive na memória e nunca foi
/// delegável — cobrá-la seria pedir o impossível.
/// </summary>
[Rule("PERF401", RuleCategory.Performance, Severity.Error)]
public sealed class DelegationRule : IRule
{
    /// <summary>
    /// Funções que o Power Apps nunca delega, seja qual for o conector. A lista
    /// é curta de propósito: delegabilidade varia por conector e por tipo de
    /// coluna, e um linter que erra num achado de severidade Erro é pior que um
    /// que fala menos.
    /// </summary>
    private static readonly HashSet<string> NeverDelegated =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Search", "First", "Last", "FirstN", "LastN", "Concat", "Collect",
            "ClearCollect", "GroupBy", "Ungroup", "AddColumns", "DropColumns",
            "ShowColumns", "RenameColumns", "Distinct", "Shuffle",
        };

    /// <summary>
    /// Funções cujo primeiro argumento é uma tabela e que delegam — mas só
    /// quando a condição também é delegável. Entram aqui para que a regra
    /// enxergue a fonte dentro delas.
    /// </summary>
    private static readonly HashSet<string> TableFunctions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Filter", "LookUp", "Sort", "SortByColumns", "Search", "First", "Last",
            "FirstN", "LastN", "CountRows", "CountIf", "Sum", "Average", "Min", "Max",
            "GroupBy", "AddColumns", "Distinct", "Concat", "Shuffle",
        };

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var externas = ExternalSourceNames(app);
            if (externas.Count == 0)
                continue;

            foreach (var property in AllProperties(app))
            {
                var root = PowerFxParser.Parse(property.Script).Root;
                if (root is null)
                    continue;

                foreach (var call in AstWalker.Descendants(root).OfType<CallNode>())
                {
                    var nome = AstWalker.FunctionName(call);
                    if (nome is null || !TableFunctions.Contains(nome))
                        continue;

                    var args = call.Args?.ChildNodes;
                    if (args is null || args.Count == 0)
                        continue;

                    var fonte = SourceNameOf(args[0]);
                    if (fonte is null || !externas.Contains(fonte))
                        continue;

                    // Toda chamada sobre fonte externa é um alvo, inclusive as
                    // que passam: sem isso a conformidade da regra seria sempre
                    // 0% quando ela achasse algo.
                    ctx.Evaluated(1);

                    if (!NeverDelegated.Contains(nome))
                        continue;

                    ctx.Report(
                        property.Location,
                        $"'{nome}' não é delegável, e aqui ela é aplicada sobre '{fonte}', que é uma "
                        + "fonte externa. O app vai trazer só os primeiros registros e calcular o resto "
                        + "no dispositivo — sem erro, com a resposta errada assim que a tabela crescer.");
                }
            }
        }
    }

    /// <summary>
    /// O nome da fonte no primeiro argumento. Aceita a fonte direta e a fonte
    /// dentro de outra função de tabela — 'Sort(Filter(Pedidos, …), …)' também
    /// consulta Pedidos.
    /// </summary>
    private static string? SourceNameOf(TexlNode node) => node switch
    {
        FirstNameNode first => first.Ident.Name.Value,
        CallNode call when AstWalker.FunctionName(call) is { } nome && TableFunctions.Contains(nome)
            => call.Args?.ChildNodes is { Count: > 0 } args ? SourceNameOf(args[0]) : null,
        _ => null,
    };

    /// <summary>
    /// Fontes que vivem fora do dispositivo. Coleções e amostras do Studio não
    /// entram: elas nunca foram delegáveis, e cobrá-las seria pedir o impossível.
    /// </summary>
    private static HashSet<string> ExternalSourceNames(CanvasApp app) =>
        app.DataSources
            .Where(d => !d.Kind.Contains("Collection", StringComparison.OrdinalIgnoreCase)
                        && !d.Kind.Contains("Static", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}
