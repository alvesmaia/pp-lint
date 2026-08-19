using System.Globalization;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Naming;

/// <summary>
/// NM040 — nome com acento, cedilha, til, espaço, vírgula ou qualquer coisa fora
/// de letra ASCII, dígito e underscore.
///
/// Não é questão de estilo, e por isso é erro. Em Power Fx, um nome fora desse
/// alfabeto obriga a citar o identificador entre aspas simples em toda
/// referência, e um apóstrofo mal fechado numa fórmula distante quebra outra
/// coisa. Em coluna de SharePoint, o nome interno recebe a codificação
/// '_x00e7_' e deixa de ser o que aparece na tela. Em nome lógico do Dataverse,
/// o caractere simplesmente não é aceito.
/// </summary>
[Rule("NM040", RuleCategory.Naming, Severity.Error)]
public sealed class SpecialCharacterNameRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var control in app.AllControls())
                Verificar(ctx, control.Name, control.Location, DescreverControle(control));

            // Um nome de variável aparece em quantas fórmulas quiserem, mas é um
            // nome só: reportar por ocorrência daria mais violações que alvos e
            // quebraria a invariante do índice.
            var graph = VariableGraph.Build(app);

            foreach (var grupo in graph.Definitions.GroupBy(d => d.Name, StringComparer.Ordinal))
            {
                var definicao = grupo.First();
                Verificar(ctx, definicao.Name, definicao.Location, Descrever(definicao.Kind));
            }
        }

        foreach (var table in ctx.Project.Tables)
        {
            Verificar(ctx, table.SchemaName, table.Location, "A tabela");

            // Colunas do esquema padrão não foram escolhidas por ninguém, e
            // várias já nascem fora do alfabeto que a regra exige.
            foreach (var column in table.Columns.Where(c => c.IsCustom && c.DerivedFrom is null))
                Verificar(ctx, column.SchemaName, table.Location, "A coluna");
        }

        foreach (var flow in ctx.Project.Flows)
        {
            Verificar(ctx, flow.Name, flow.Location, "O fluxo");

            foreach (var variable in flow.Variables)
                Verificar(ctx, variable.Name, variable.Location, "A variável de fluxo");

            foreach (var action in flow.AllActions())
                Verificar(ctx, action.Name, action.Location, "A ação");
        }
    }

    private static void Verificar(LintContext ctx, string nome, SourceLocation location, string descricao)
    {
        ctx.Evaluated(1);

        var invalidos = CaracteresInvalidos(nome);
        if (invalidos.Count == 0)
            return;

        ctx.Report(
            location,
            $"{descricao} '{nome}' usa {Listar(invalidos)}. Use apenas letras sem acento, "
            + "dígitos e underscore: fora desse alfabeto o nome precisa de aspas em toda "
            + "referência, e vira código no nome interno da coluna.");
    }

    /// <summary>
    /// Os caracteres fora de [A-Za-z0-9_], sem repetição e na ordem em que
    /// aparecem — é o que o usuário precisa procurar no nome.
    /// </summary>
    private static IReadOnlyList<char> CaracteresInvalidos(string nome)
    {
        var vistos = new HashSet<char>();
        var invalidos = new List<char>();

        foreach (var c in nome)
        {
            if (Permitido(c) || !vistos.Add(c))
                continue;

            invalidos.Add(c);
        }

        return invalidos;
    }

    private static bool Permitido(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

    private static string Listar(IReadOnlyList<char> invalidos)
    {
        var citados = invalidos.Select(Citar).ToList();

        if (citados.Count == 1)
            return $"o caractere {citados[0]}";

        return "os caracteres " + string.Join(", ", citados[..^1]) + " e " + citados[^1];
    }

    /// <summary>
    /// Espaço entre aspas passa despercebido no terminal; nomeá-lo é a única
    /// forma de o usuário enxergar o problema.
    /// </summary>
    private static string Citar(char c) => c switch
    {
        ' ' => "espaço",
        '\t' => "tabulação",
        _ => $"'{c}'",
    };

    private static string DescreverControle(Control control) =>
        control.IsScreen ? "A tela" : "O controle";

    private static string Descrever(VariableKind kind) => kind switch
    {
        VariableKind.Global => "A variável global",
        VariableKind.Context => "A variável de contexto",
        _ => "A coleção",
    };
}
