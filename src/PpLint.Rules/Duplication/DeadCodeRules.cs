using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Duplication;

/// <summary>
/// Tudo que estas regras precisam saber sobre quem é citado onde.
/// </summary>
internal static class ReferenceIndex
{
    /// <summary>
    /// Todo identificador que aparece em alguma fórmula do app, junto com o
    /// texto bruto das fórmulas — algumas referências vivem dentro de string,
    /// e o parser não as enxerga como identificador.
    /// </summary>
    public static HashSet<string> NamesUsedIn(CanvasApp app)
    {
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in AllProperties(app))
        {
            var root = PowerFxParser.Parse(property.Script).Root;
            if (root is null)
                continue;

            foreach (var identifier in AstWalker.Identifiers(root))
                usados.Add(identifier.Ident.Name.Value);

            // Nome de coluna e de propriedade aparecem à direita de um ponto —
            // 'ThisItem.Nome', 'btnOk.Text' — e não como identificador solto.
            foreach (var node in AstWalker.Descendants(root).OfType<Microsoft.PowerFx.Syntax.DottedNameNode>())
                usados.Add(node.Right.Name.Value);

            // Numa chamada qualificada como 'Office365Users.UserPhotoV2(x)', o
            // parser guarda o conector no namespace da função, e não como nó da
            // árvore. Sem olhar aqui, todo conector usado só por função pareceria
            // não usado — foi o falso positivo que o app real revelou.
            foreach (var call in AstWalker.Descendants(root).OfType<Microsoft.PowerFx.Syntax.CallNode>())
            {
                var ns = call.Head?.Namespace.ToString();
                if (!string.IsNullOrEmpty(ns))
                    foreach (var parte in ns.Split('.', StringSplitOptions.RemoveEmptyEntries))
                        usados.Add(parte);
            }
        }

        return usados;
    }

    public static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}

/// <summary>
/// DUP304 — tela para a qual nenhum <c>Navigate</c> aponta. Ela continua sendo
/// carregada, publicada e mantida, e ninguém consegue abri-la.
/// </summary>
[Rule("DUP304", RuleCategory.Duplication, Severity.Warning)]
public sealed class UnreachableScreenRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var alvos = NavigationTargets(app);

            // A primeira tela é a que abre quando o app inicia: ninguém navega
            // para ela, e exigir isso seria acusar todo app existente.
            foreach (var screen in app.Screens.Skip(1))
            {
                ctx.Evaluated(1);

                if (alvos.Contains(screen.Name))
                    continue;

                ctx.Report(
                    screen.Location,
                    $"Nenhum Navigate aponta para a tela '{screen.Name}'. Ela continua sendo publicada "
                    + "e mantida sem que ninguém consiga abri-la — ou falta a navegação, ou a tela "
                    + "pode sair.");
            }
        }
    }

    /// <summary>
    /// Os nomes citados no primeiro argumento de Navigate e de Back. O nome vem
    /// entre aspas simples quando tem espaço, e o Render as preserva.
    /// </summary>
    private static HashSet<string> NavigationTargets(CanvasApp app)
    {
        var alvos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in ReferenceIndex.AllProperties(app))
        {
            var root = PowerFxParser.Parse(property.Script).Root;
            if (root is null)
                continue;

            foreach (var call in AstWalker.Calls(root, "Navigate"))
            {
                var args = call.Args?.ChildNodes;
                if (args is { Count: > 0 })
                    alvos.Add(AstComparer.Render(args[0]).Trim('\''));
            }
        }

        return alvos;
    }
}

/// <summary>
/// DUP305 — fonte de dados declarada no app e nunca consultada. Ela pesa no
/// tempo de abertura e aparece na lista de dependências da solução, obrigando
/// quem faz o deploy a criar uma conexão que ninguém usa.
/// </summary>
[Rule("DUP305", RuleCategory.Duplication, Severity.Warning)]
public sealed class UnusedDataSourceRule : IRule
{
    /// <summary>
    /// Coleções e dados de exemplo do Studio também aparecem em
    /// DataSources.json. Coleção morta é assunto da PF103, que sabe onde ela
    /// foi criada; amostra do Studio não é escolha de ninguém.
    /// </summary>
    private static bool IsExternalSource(DataSource source) =>
        !source.Kind.Contains("Collection", StringComparison.OrdinalIgnoreCase)
        && !source.Kind.Contains("Static", StringComparison.OrdinalIgnoreCase);

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var usados = ReferenceIndex.NamesUsedIn(app);

            foreach (var source in app.DataSources.Where(IsExternalSource))
            {
                ctx.Evaluated(1);

                if (usados.Contains(source.Name))
                    continue;

                ctx.Report(
                    app.Location,
                    $"A fonte de dados '{source.Name}' está declarada no app e nunca é consultada. "
                    + "Ela entra na lista de dependências da solução, e quem faz o deploy precisa "
                    + "criar uma conexão para algo que ninguém usa.");
            }
        }
    }
}

/// <summary>
/// DUP303 — controle que nenhuma fórmula cita e que nunca fica visível.
/// </summary>
[Rule("DUP303", RuleCategory.Duplication, Severity.Warning)]
public sealed class DeadControlRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var usados = ReferenceIndex.NamesUsedIn(app);

            foreach (var control in app.AllControls())
            {
                // Tela é assunto da DUP304, que sabe olhar para Navigate.
                if (control.IsScreen)
                    continue;

                // Um controle que contém outros existe pelo arranjo, não por si;
                // e um filho visível já basta para o container importar.
                if (control.Children.Count > 0)
                    continue;

                var invisivel = IsAlwaysInvisible(control);
                if (!invisivel)
                    continue;

                ctx.Evaluated(1);

                if (usados.Contains(control.Name))
                    continue;

                ctx.Report(
                    control.Location,
                    $"O controle '{control.Name}' tem Visible fixo em false e nenhuma fórmula o cita. "
                    + "Ele é carregado com a tela sem nunca aparecer nem ser lido — código morto "
                    + "que sobra de uma versão anterior.");
            }
        }
    }

    /// <summary>
    /// Visible escrito como o literal false. Uma expressão qualquer ali pode ser
    /// verdadeira em algum momento, e chamar o controle de morto seria errado.
    /// </summary>
    private static bool IsAlwaysInvisible(Control control)
    {
        var visible = control.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, "Visible", StringComparison.OrdinalIgnoreCase));

        if (visible is null)
            return false;

        var root = PowerFxParser.Parse(visible.Script).Root;
        return root is Microsoft.PowerFx.Syntax.BoolLitNode { Value: false };
    }
}
