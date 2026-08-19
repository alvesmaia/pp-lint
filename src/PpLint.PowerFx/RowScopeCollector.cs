using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx;

/// <summary>
/// Nomes que existem só dentro da expressão: ThisItem numa galeria, o apelido
/// de um `As`, os campos de um `With`. Não são variáveis do app e não podem
/// ser cobrados como tal.
/// </summary>
public static class RowScopeCollector
{
    /// <summary>
    /// Nomes que o Power Fx introduz sozinho. "Value" entra porque é a coluna
    /// implícita de tabelas de valor único — Concat([1,2], Value) é código correto.
    /// </summary>
    private static readonly string[] Implicit =
        ["ThisItem", "ThisRecord", "Self", "Parent", "ThisProperty", "Value"];

    public static IReadOnlySet<string> Collect(TexlNode root)
    {
        var scopes = new HashSet<string>(Implicit, StringComparer.OrdinalIgnoreCase);

        // Pedidos As pedido
        foreach (var asNode in AstWalker.Descendants(root).OfType<AsNode>())
        {
            var alias = asNode.Right.Name.Value;
            if (!string.IsNullOrEmpty(alias))
                scopes.Add(alias);
        }

        // With({total: 10}, total * 2) — só o record do With vira escopo.
        // Um record qualquer NÃO serve: UpdateContext({locFiltro: 1}) também é
        // um record, e tratar seus campos como escopo faria as variáveis de
        // contexto sumirem do grafo — PF102 e NM002 nunca veriam nada.
        foreach (var call in AstWalker.Calls(root, "With"))
        {
            var args = call.Args?.ChildNodes;
            if (args is null || args.Count == 0 || args[0] is not RecordNode record)
                continue;

            foreach (var id in record.Ids)
            {
                var field = id.Name.Value;
                if (!string.IsNullOrEmpty(field))
                    scopes.Add(field);
            }
        }

        return scopes;
    }
}
