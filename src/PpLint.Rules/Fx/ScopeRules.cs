using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Fx;

/// <summary>
/// PF120 — <c>UpdateContext</c> no <c>App.OnStart</c>. Variável de contexto
/// pertence a uma tela, e o App não é uma tela: a chamada não define nada.
/// </summary>
[Rule("PF120", RuleCategory.PowerFx, Severity.Error)]
public sealed class ContextVariableInOnStartRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var property in app.AppProperties)
            {
                var root = PowerFxParser.Parse(property.Script).Root;
                if (root is null)
                    continue;

                ctx.Evaluated(1);

                var chamada = AstWalker.Calls(root, "UpdateContext").FirstOrDefault();
                if (chamada is null)
                    continue;

                ctx.Report(
                    property.Location,
                    $"'{property.Name}' chama UpdateContext, mas variável de contexto pertence a uma "
                    + "tela e o App não é uma tela — a chamada não define nada, e quem lê a variável "
                    + "depois recebe branco. Use Set para estado do app inteiro.");
            }
        }
    }
}

/// <summary>
/// PF121 — fórmula que lê uma propriedade de controle que vive em outra tela.
///
/// O valor só existe depois que aquela tela é carregada. Antes disso a leitura
/// devolve branco, sem erro — e o app funciona ou não conforme o caminho que o
/// usuário percorreu até ali.
/// </summary>
[Rule("PF121", RuleCategory.PowerFx, Severity.Warning)]
public sealed class CrossScreenReferenceRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var donoDaTela = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Só controles entram. Ler a dimensão de outra tela — 'Settings
            // Screen'.Width — é referência de layout, e resolve sem a tela estar
            // carregada; o que não resolve é o valor guardado num controle dela.
            foreach (var screen in app.Screens)
                foreach (var control in screen.SelfAndDescendants().Where(c => !c.IsScreen))
                    donoDaTela[control.Name] = screen.Name;

            foreach (var screen in app.Screens)
            {
                foreach (var control in screen.SelfAndDescendants())
                {
                    foreach (var property in control.Properties)
                    {
                        var root = PowerFxParser.Parse(property.Script).Root;
                        if (root is null)
                            continue;

                        foreach (var citado in ReferencedControls(root))
                        {
                            if (!donoDaTela.TryGetValue(citado, out var dono))
                                continue;

                            ctx.Evaluated(1);

                            if (string.Equals(dono, screen.Name, StringComparison.OrdinalIgnoreCase))
                                continue;

                            ctx.Report(
                                property.Location,
                                $"'{control.Name}.{property.Name}' lê '{citado}', que vive na tela "
                                + $"'{dono}'. O valor só existe depois que aquela tela for carregada; "
                                + "antes disso a leitura devolve branco sem dar erro. Passe o valor por "
                                + "variável.");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Os nomes à esquerda de um ponto — 'btnOk.Text' cita 'btnOk'. É assim que
    /// uma fórmula lê a propriedade de outro controle.
    /// </summary>
    private static IEnumerable<string> ReferencedControls(TexlNode root) =>
        AstWalker.Descendants(root)
            .OfType<DottedNameNode>()
            .Select(n => n.Left)
            .OfType<FirstNameNode>()
            .Select(n => n.Ident.Name.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// PF130 — a expressão não compila. O parser do Power Fx recusou a fórmula.
/// </summary>
[Rule("PF130", RuleCategory.PowerFx, Severity.Error)]
public sealed class UnparsableFormulaRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var (property, dono) in AllProperties(app))
            {
                if (string.IsNullOrWhiteSpace(property.Script))
                    continue;

                ctx.Evaluated(1);

                var resultado = PowerFxParser.Parse(property.Script);
                if (resultado.IsSuccess)
                    continue;

                ctx.Report(
                    property.Location,
                    $"A fórmula de '{dono}.{property.Name}' não compila: {resultado.Errors.FirstOrDefault() ?? "erro não descrito"}. "
                    + "Toda regra de Power Fx desta análise pula fórmulas que não compilam, então "
                    + "este trecho não foi examinado por nenhuma outra.");
            }
        }
    }

    private static IEnumerable<(PowerFxProperty Property, string Dono)> AllProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return (property, "App");

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return (property, control.Name);
    }
}
