using System.Text.RegularExpressions;
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF122 — <c>ForAll</c> com <c>Collect</c> ou <c>Patch</c> dentro: uma escrita
/// por item, uma ida à rede por item.
/// </summary>
[Rule("PF122", RuleCategory.PowerFx, Severity.Warning)]
public sealed class ForAllWithWriteRule : IRule
{
    private static readonly HashSet<string> WriteFunctions =
        new(StringComparer.OrdinalIgnoreCase) { "Collect", "Patch", "Remove", "Update", "UpdateIf" };

    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            foreach (var forAll in AstWalker.Calls(root, "ForAll"))
            {
                ctx.Evaluated(1);

                var escrita = AstWalker.Descendants(forAll)
                    .OfType<CallNode>()
                    .Select(AstWalker.FunctionName)
                    .OfType<string>()
                    .FirstOrDefault(WriteFunctions.Contains);

                if (escrita is null)
                    continue;

                ctx.Report(
                    property.Location,
                    $"Este ForAll chama '{escrita}' a cada item: são tantas idas à rede quantos forem "
                    + "os registros. Collect e Patch aceitam uma tabela inteira de uma vez — passe o "
                    + "resultado do ForAll para a escrita, em vez de escrever dentro dele.");
            }
        }
    }
}

/// <summary>
/// PF123 — expressão aninhada além do que se lê de uma vez.
/// </summary>
[Rule("PF123", RuleCategory.PowerFx, Severity.Warning)]
public sealed class DeepNestingRule : IRule
{
    /// <summary>
    /// Profundidade de chamadas aninhadas a partir da qual a fórmula deixa de
    /// caber na cabeça de quem lê. Não é limite técnico — o Power Fx aceita
    /// muito mais — é limite de manutenção.
    /// </summary>
    private const int MaxDepth = 6;

    public void Check(LintContext ctx)
    {
        foreach (var (property, root) in LogicHelpers.ParsedProperties(ctx))
        {
            ctx.Evaluated(1);

            var profundidade = CallDepth(root);
            if (profundidade <= MaxDepth)
                continue;

            ctx.Report(
                property.Location,
                $"A fórmula aninha chamadas {profundidade} níveis, acima do limite de {MaxDepth}. "
                + "Quem for alterá-la precisa segurar todos os níveis na cabeça ao mesmo tempo — "
                + "quebre em passos, com variáveis nomeadas ou With().");
        }
    }

    /// <summary>
    /// O maior encadeamento de chamadas dentro de chamadas. Conta só CallNode:
    /// parênteses e operadores aninham a árvore sem aumentar o que custa ler.
    /// </summary>
    private static int CallDepth(TexlNode node)
    {
        var filhos = node switch
        {
            CallNode call => call.Args?.ChildNodes ?? [],
            _ => AstWalker.Descendants(node).Skip(1).Where(n => n.Parent == node).ToList(),
        };

        var maior = 0;
        foreach (var filho in filhos)
            maior = Math.Max(maior, CallDepth(filho));

        return node is CallNode ? maior + 1 : maior;
    }
}

/// <summary>
/// PF124 — a mesma cor literal escrita em muitos lugares.
///
/// A regra só fala quando a cor se repete: uma cor usada uma vez é escolha
/// local, e num app com ilustração vetorial as cores literais chegam às
/// dezenas sem que nada esteja errado. O que merece aviso é a cor que virou
/// padrão de fato sem nunca ter sido declarada como tal.
/// </summary>
[Rule("PF124", RuleCategory.PowerFx, Severity.Info)]
public sealed class HardcodedColorRule : IRule
{
    /// <summary>
    /// A partir de quantos usos a cor deixa de ser detalhe e vira decisão de
    /// tema. Abaixo disso, extrair daria mais trabalho que benefício.
    /// </summary>
    private const int MinUses = 5;

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var porCor = new Dictionary<string, (int Vezes, SourceLocation Onde)>(StringComparer.Ordinal);

            foreach (var property in AllProperties(app))
            {
                var root = PowerFxParser.Parse(property.Script).Root;
                if (root is null)
                    continue;

                foreach (var call in AstWalker.Descendants(root).OfType<CallNode>())
                {
                    var nome = AstWalker.FunctionName(call);
                    if (nome is null || !nome.Equals("RGBA", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var texto = AstComparer.Render(call);
                    var atual = porCor.TryGetValue(texto, out var v) ? v : (Vezes: 0, Onde: property.Location);
                    porCor[texto] = (atual.Vezes + 1, atual.Onde);
                }
            }

            foreach (var (cor, (vezes, onde)) in porCor)
            {
                ctx.Evaluated(1);

                if (vezes < MinUses)
                    continue;

                ctx.Report(
                    onde,
                    $"A cor '{cor}' aparece {vezes} vezes escrita à mão. Ela já é um padrão do app "
                    + "sem nunca ter sido declarada como tal: guarde-a numa variável de tema, para "
                    + $"que mudá-la seja uma edição em vez de {vezes}.");
            }
        }
    }

    private static IEnumerable<PowerFxProperty> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}

/// <summary>
/// PF125 — texto exibido ao usuário escrito direto na fórmula, num app que já
/// tem tabela de tradução.
///
/// A regra fica calada em app sem tradução nenhuma: ali o texto literal é a
/// escolha certa, e avisar seria empurrar trabalho que ninguém pediu. Ela só
/// fala quando alguém começou a traduzir e deixou textos para trás — que é
/// quando o app aparece meio traduzido para o usuário.
/// </summary>
[Rule("PF125", RuleCategory.PowerFx, Severity.Info)]
public sealed class UntranslatedTextRule : IRule
{
    /// <summary>
    /// Nomes que denunciam uma tabela de tradução. Se nenhum aparece, o app não
    /// traduz nada e a regra não tem o que cobrar.
    /// </summary>
    private static readonly Regex TranslationSource = new(
        @"\b(?:translation|translations|locali[sz]ation|resources?|traducao|traducoes|idioma|labels?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Propriedades cujo conteúdo o usuário lê na tela.
    /// </summary>
    private static readonly HashSet<string> UserFacing =
        new(StringComparer.OrdinalIgnoreCase) { "Text", "HintText", "Tooltip", "AccessibleLabel" };

    /// <summary>Texto curto demais para ser frase costuma ser código ou símbolo.</summary>
    private const int MinLength = 8;

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            if (!UsesTranslation(app))
                continue;

            foreach (var control in app.AllControls())
            {
                foreach (var property in control.Properties.Where(p => UserFacing.Contains(p.Name)))
                {
                    var root = PowerFxParser.Parse(property.Script).Root;
                    if (root is not StrLitNode literal)
                        continue;

                    ctx.Evaluated(1);

                    if (literal.Value.Trim().Length < MinLength)
                        continue;

                    ctx.Report(
                        property.Location,
                        $"'{control.Name}.{property.Name}' mostra o texto \"{Trim(literal.Value)}\" escrito "
                        + "direto na fórmula, e este app tem tabela de tradução. O usuário vê a tela meio "
                        + "traduzida — mova o texto para a tabela.");
                }
            }
        }
    }

    private static bool UsesTranslation(CanvasApp app) =>
        app.DataSources.Any(d => TranslationSource.IsMatch(d.Name));

    private static string Trim(string texto) =>
        texto.Length <= 40 ? texto : texto[..39] + "…";
}
