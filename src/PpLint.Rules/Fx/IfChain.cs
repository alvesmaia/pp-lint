using Microsoft.PowerFx.Syntax;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// Um desvio condicional em forma normalizada: os pares condição/resultado na
/// ordem em que são testados, e o resultado final quando nenhum vale.
///
/// Power Fx escreve o mesmo desvio de duas maneiras — encadeada,
/// If(c1, r1, c2, r2, senão), e aninhada, If(c1, r1, If(c2, r2, senão)) — e o
/// Studio gera as duas. Analisar só uma delas deixaria metade do código real
/// passar sem exame.
/// </summary>
internal sealed record IfChain(
    IReadOnlyList<(TexlNode Condition, TexlNode Result)> Pairs,
    TexlNode? Else)
{
    /// <summary>Os resultados possíveis, incluindo o 'senão' quando existe.</summary>
    public IEnumerable<TexlNode> Results =>
        Pairs.Select(p => p.Result).Concat(Else is null ? [] : [Else]);

    /// <summary>
    /// Achata o If em pares, absorvendo os Ifs aninhados no ramo 'senão'.
    /// Devolve null quando o nó não é um If utilizável.
    /// </summary>
    public static IfChain? From(CallNode call)
    {
        var pares = new List<(TexlNode, TexlNode)>();
        var atual = call;

        while (true)
        {
            var args = atual.Args?.ChildNodes;
            if (args is null || args.Count < 2)
                return pares.Count > 0 ? new IfChain(pares, null) : null;

            var completos = args.Count / 2;
            for (var i = 0; i < completos; i++)
                pares.Add((args[i * 2], args[i * 2 + 1]));

            // Contagem par: não há 'senão'. Ímpar: o último argumento é ele.
            if (args.Count % 2 == 0)
                return new IfChain(pares, null);

            var senao = args[^1];
            if (senao is CallNode aninhado && IsIf(aninhado))
            {
                atual = aninhado;
                continue;
            }

            return new IfChain(pares, senao);
        }
    }

    /// <summary>
    /// Os Ifs que são o ramo 'senão' de outro If. Já foram absorvidos pela
    /// cadeia do pai, então analisá-los de novo contaria o mesmo desvio várias
    /// vezes e distorceria a conformidade da regra.
    /// </summary>
    public static HashSet<CallNode> NestedElseIfs(TexlNode root)
    {
        var aninhados = new HashSet<CallNode>();

        foreach (var call in AstWalker.Descendants(root).OfType<CallNode>())
        {
            if (!IsIf(call))
                continue;

            var args = call.Args?.ChildNodes;
            if (args is { Count: > 2 } && args.Count % 2 == 1
                && args[^1] is CallNode senao && IsIf(senao))
            {
                aninhados.Add(senao);
            }
        }

        return aninhados;
    }

    private static bool IsIf(CallNode call) =>
        string.Equals(AstWalker.FunctionName(call), "If", StringComparison.OrdinalIgnoreCase);
}
