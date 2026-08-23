using System.Text.RegularExpressions;
using Microsoft.PowerFx.Syntax;
using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.PowerFx;

namespace PpLint.Rules.Security;

/// <summary>
/// Os textos literais de um projeto, venham do app ou do fluxo, com onde cada
/// um foi escrito.
/// </summary>
internal static class LiteralTexts
{
    public static IEnumerable<(string Text, SourceLocation Location, string Context)> All(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var property in AppProperties(app))
            {
                var root = PowerFxParser.Parse(property.Script).Root;
                if (root is null)
                    continue;

                foreach (var literal in AstWalker.Descendants(root).OfType<StrLitNode>())
                    yield return (literal.Value, property.Location, property.Location.Symbol ?? "fórmula");
            }
        }

        foreach (var flow in ctx.Project.Flows)
            foreach (var action in flow.AllActions())
                foreach (var expression in action.Expressions)
                    yield return (expression, action.Location, action.Name);
    }

    private static IEnumerable<PowerFxProperty> AppProperties(CanvasApp app)
    {
        foreach (var property in app.AppProperties)
            yield return property;

        foreach (var control in app.AllControls())
            foreach (var property in control.Properties)
                yield return property;
    }
}

/// <summary>
/// SEC501 — segredo, chave ou token escrito à mão numa fórmula ou num input de
/// fluxo.
///
/// Um artefato exportado circula por e-mail, por repositório e por ticket de
/// suporte. Quem recebe o arquivo recebe a credencial junto, e rotacioná-la
/// depois exige saber por onde o arquivo passou.
/// </summary>
[Rule("SEC501", RuleCategory.Security, Severity.Error)]
public sealed class HardcodedSecretRule : IRule
{
    /// <summary>
    /// Formas que só existem para carregar credencial. Cada uma foi escolhida
    /// por ter estrutura reconhecível: procurar a palavra "senha" acusaria todo
    /// rótulo de formulário de login.
    /// </summary>
    private static readonly (Regex Pattern, string Descricao)[] Signatures =
    [
        (new Regex(@"^ey[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.", RegexOptions.Compiled),
         "um token JWT"),

        (new Regex(@"\b(?:sig|sv)=[A-Za-z0-9%+/=]{20,}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
         "uma assinatura de acesso compartilhado"),

        (new Regex(@"\bAccountKey=[A-Za-z0-9+/=]{20,}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
         "uma chave de conta de armazenamento"),

        (new Regex(@"\b(?:api[_-]?key|apikey|client[_-]?secret|access[_-]?token|bearer)\b\s*[:=]\s*['""]?[A-Za-z0-9_\-]{16,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
         "uma chave de API ou segredo de cliente"),

        (new Regex(@"^(?:xox[baprs]-[A-Za-z0-9-]{10,}|gh[pousr]_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16})$",
            RegexOptions.Compiled),
         "uma credencial de serviço conhecida"),
    ];

    public void Check(LintContext ctx)
    {
        foreach (var (texto, location, contexto) in LiteralTexts.All(ctx))
        {
            // Todo texto literal é um alvo: a esmagadora maioria passa, e é isso
            // que faz a conformidade desta regra significar algo.
            ctx.Evaluated(1);

            var assinatura = Signatures.FirstOrDefault(s => s.Pattern.IsMatch(texto));
            if (assinatura.Pattern is null)
                continue;

            ctx.Report(
                location,
                $"'{contexto}' contém {assinatura.Descricao} escrito à mão. O artefato exportado "
                + "circula por e-mail, repositório e ticket de suporte, levando a credencial junto. "
                + "Use uma variável de ambiente ou o Azure Key Vault, e rotacione esta chave.");
        }
    }
}

/// <summary>
/// SEC502 — endereço de ambiente fixo na fórmula. Ele não muda ao promover a
/// solução, e o app de produção continua conversando com desenvolvimento.
/// </summary>
[Rule("SEC502", RuleCategory.Security, Severity.Warning)]
public sealed class HardcodedEnvironmentUrlRule : IRule
{
    /// <summary>
    /// Endereços que identificam um ambiente ou um tenant específico. Um
    /// endereço público qualquer não conta: link para documentação é uso
    /// legítimo e não muda entre ambientes.
    /// </summary>
    private static readonly Regex EnvironmentUrl = new(
        @"https?://[A-Za-z0-9-]+\.(?:crm[0-9]*\.dynamics\.com"
        + @"|(?:sharepoint|api\.crm)\.com"
        + @"|[A-Za-z0-9-]+\.sharepoint\.com"
        + @"|azurewebsites\.net"
        + @"|logic\.azure\.com)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Check(LintContext ctx)
    {
        foreach (var (texto, location, contexto) in LiteralTexts.All(ctx))
        {
            ctx.Evaluated(1);

            var match = EnvironmentUrl.Match(texto);
            if (!match.Success)
                continue;

            ctx.Report(
                location,
                $"'{contexto}' aponta para '{match.Value}', que identifica um ambiente específico. "
                + "Ao promover a solução esse endereço não muda, e o app de produção continua "
                + "conversando com o ambiente antigo. Use uma variável de ambiente.");
        }
    }
}

/// <summary>
/// SEC503 — conector que atravessa a fronteira da organização usado junto com
/// conectores de dados internos.
/// </summary>
[Rule("SEC503", RuleCategory.Security, Severity.Warning)]
public sealed class RiskyConnectorRule : IRule
{
    /// <summary>
    /// Conectores que publicam para fora. Nenhum deles é proibido — o que a
    /// regra pede é que a escolha seja consciente, porque é por eles que dado
    /// interno sai da organização sem passar por nenhuma política.
    /// </summary>
    private static readonly HashSet<string> Outbound =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Twitter", "Facebook", "Instagram", "Dropbox", "Box", "GoogleDrive",
            "GoogleSheets", "GoogleTasks", "Gmail", "Slack", "Trello", "Bitly",
            "Pinterest", "Reddit", "Youtube", "SmtpConnector", "FTP", "SFTP",
        };

    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            foreach (var source in app.DataSources)
            {
                ctx.Evaluated(1);

                if (!Outbound.Contains(source.Name))
                    continue;

                ctx.Report(
                    app.Location,
                    $"O app usa o conector '{source.Name}', que envia dados para fora da organização. "
                    + "Confirme que essa saída é intencional e que a política de prevenção de perda "
                    + "de dados do ambiente a permite — é por conectores assim que dado interno sai "
                    + "sem passar por nenhuma revisão.");
            }
        }
    }
}
