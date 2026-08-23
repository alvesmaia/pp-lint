using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Duplication;

/// <summary>
/// DUP301 — a mesma fórmula escrita por extenso em vários lugares. Quando a
/// regra de negócio dentro dela mudar, alguém vai corrigir uma cópia e esquecer
/// as outras; o app passa a se comportar de dois jeitos.
///
/// Duas decisões que o app real impôs:
///
/// Compara fórmulas inteiras, não sub-expressões. Num app com SVG,
/// 'Min(viewBox.Width, viewBox.Height)' aparece 66 vezes dentro de fórmulas
/// diferentes — é uso normal de um valor, não duplicação de lógica.
///
/// Ignora as propriedades que o Studio escreve sozinho em toda tela. 'Size' e
/// 'Orientation' nascem idênticas em cada tela criada, e cobrá-las seria
/// reclamar de código que ninguém escreveu.
/// </summary>
[Rule("DUP301", RuleCategory.Duplication, Severity.Warning)]
public sealed class DuplicateFormulaRule : IRule
{
    /// <summary>
    /// Propriedades geradas pelo Power Apps Studio ao criar uma tela ou um
    /// controle. Vêm iguais em todo lugar por construção.
    /// </summary>
    private static readonly HashSet<string> GeneratedProperties =
        new(StringComparer.OrdinalIgnoreCase) { "Size", "Orientation", "Width", "Height", "Fill" };

    /// <summary>
    /// Fórmula menor que isto não vale o aviso: 'Parent.Fill' e 'false'
    /// repetem-se por toda parte, e extrair não melhoraria nada.
    /// </summary>
    private const int MinLength = 40;

    /// <summary>
    /// A partir de quantas cópias vale falar. Duas ocorrências costumam ser
    /// coincidência; três é padrão.
    /// </summary>
    private const int MinOccurrences = 3;

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var porTexto = new Dictionary<string, List<(Control Control, PowerFxProperty Property)>>(
                StringComparer.Ordinal);

            foreach (var control in app.AllControls())
            {
                foreach (var property in control.Properties)
                {
                    if (GeneratedProperties.Contains(property.Name))
                        continue;

                    var root = PowerFxParser.Parse(property.Script).Root;
                    if (root is null)
                        continue;

                    var texto = AstComparer.Render(root);
                    if (texto.Length < MinLength)
                        continue;

                    if (!porTexto.TryGetValue(texto, out var lista))
                        porTexto[texto] = lista = [];

                    lista.Add((control, property));
                }
            }

            foreach (var (texto, ocorrencias) in porTexto)
            {
                // Cada fórmula distinta é um alvo: a maioria não se repete, e é
                // isso que faz a conformidade da regra significar algo.
                ctx.Evaluated(1);

                if (ocorrencias.Count < MinOccurrences)
                    continue;

                var onde = string.Join(", ", ocorrencias.Take(3).Select(o => $"{o.Control.Name}.{o.Property.Name}"));
                var resto = ocorrencias.Count > 3 ? $" e mais {ocorrencias.Count - 3}" : string.Empty;

                ctx.Report(
                    ocorrencias[0].Property.Location,
                    $"A fórmula '{Trim(texto)}' aparece {ocorrencias.Count} vezes: {onde}{resto}. "
                    + "Quando a regra dentro dela mudar, é fácil corrigir uma cópia e esquecer as "
                    + "outras — extraia para uma variável, um componente ou uma fórmula nomeada.");
            }
        }
    }

    private static string Trim(string texto) =>
        texto.Length <= 60 ? texto : texto[..59] + "…";
}
