using System.Globalization;
using System.Net;
using System.Text;
using PpLint.Core;
using PpLint.Core.Reporting;
using PpLint.Core.Rules;
using PpLint.Core.Scoring;
using PpLint.Rules;

namespace PpLint.Cli;

/// <summary>
/// Relatório em HTML, para circular fora do terminal — anexado a um e-mail,
/// aberto por quem decide prioridade, guardado como registro de uma revisão.
///
/// O arquivo é autocontido: CSS e script embutidos, nenhuma requisição externa.
/// Ele costuma ser aberto de um pen drive, de uma pasta de rede ou de um anexo,
/// muitas vezes sem internet — e um relatório que depende de CDN chega quebrado
/// justamente na reunião em que seria usado.
/// </summary>
public static class HtmlReporter
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private const string Repository = "https://github.com/alvesmaia/pp-lint";

    public static string Render(AnalysisRun run)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"pt-BR\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>Relatório pp-lint</title>");
        sb.Append("<style>").Append(Css).AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        RenderHeader(sb, run);
        RenderScore(sb, run);
        RenderCategories(sb, run);
        RenderArtifacts(sb, run);
        RenderFindings(sb, run);
        RenderFooter(sb);

        sb.Append("<script>").Append(Script).AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static void RenderHeader(StringBuilder sb, AnalysisRun run)
    {
        var erros = run.AllDiagnostics.Count(d => d.Severity == Severity.Error);
        var avisos = run.AllDiagnostics.Count(d => d.Severity == Severity.Warning);
        var infos = run.AllDiagnostics.Count(d => d.Severity == Severity.Info);

        sb.AppendLine("<header>");
        sb.AppendLine("<h1>Relatório pp-lint</h1>");
        sb.Append("<p class=\"resumo\">")
          .Append(Plural(erros, "erro", "erros")).Append(", ")
          .Append(Plural(avisos, "aviso", "avisos")).Append(" e ")
          .Append(Plural(infos, "informação", "informações"))
          .Append(" em ")
          .Append(Escape(Plural(run.Artifacts.Count, "artefato", "artefatos")))
          .Append(", analisados em ")
          .Append(Escape(run.Elapsed.TotalSeconds.ToString("0.0", Br)))
          .AppendLine(" s.</p>");
        sb.AppendLine("</header>");
    }

    private static void RenderScore(StringBuilder sb, AnalysisRun run)
    {
        var score = run.Compliance.Overall;
        var conformes = score.EvaluatedTargets - score.Violations;

        sb.AppendLine("<section class=\"indice\">");
        sb.Append("<div class=\"numero ").Append(FaixaDe(score.Percent)).Append("\">")
          .Append(Percent(score.Percent)).AppendLine("</div>");
        sb.Append("<p>").Append(Escape(conformes.ToString(Br))).Append(" de ")
          .Append(Escape(score.EvaluatedTargets.ToString(Br)))
          .AppendLine(" verificações passaram.</p>");
        sb.AppendLine(
            "<p class=\"nota\">O índice é a proporção de checagens aprovadas sobre checagens "
            + "realizadas, ponderada por severidade. Não é nota de qualidade do app: é o que "
            + "estas regras conseguiram verificar.</p>");
        sb.AppendLine("</section>");
    }

    private static void RenderCategories(StringBuilder sb, AnalysisRun run)
    {
        if (run.Compliance.ByCategory.Count == 0)
            return;

        sb.AppendLine("<section>");
        sb.AppendLine("<h2>Por categoria</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>Categoria</th><th>Índice</th><th class=\"num\">Conformes</th>"
                      + "<th class=\"barra-col\"></th></tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var (categoria, score) in run.Compliance.ByCategory.OrderBy(p => Nome(p.Key), StringComparer.Ordinal))
        {
            var conformes = score.EvaluatedTargets - score.Violations;

            sb.Append("<tr><td>").Append(Escape(Nome(categoria))).Append("</td>")
              .Append("<td class=\"").Append(FaixaDe(score.Percent)).Append("\">")
              .Append(Percent(score.Percent)).Append("</td>")
              .Append("<td class=\"num\">").Append(Escape(conformes.ToString(Br))).Append(" / ")
              .Append(Escape(score.EvaluatedTargets.ToString(Br))).Append("</td>")
              .Append("<td class=\"barra-col\">").Append(Barra(score.Percent)).AppendLine("</td></tr>");
        }

        sb.AppendLine("</tbody></table></section>");
    }

    private static void RenderArtifacts(StringBuilder sb, AnalysisRun run)
    {
        // Com um artefato só, esta tabela repete o índice geral logo acima.
        if (run.Artifacts.Count < 2)
            return;

        sb.AppendLine("<section>");
        sb.AppendLine("<h2>Por artefato</h2>");
        sb.AppendLine("<p class=\"nota\">Ordenado do pior para o melhor: é por onde começar.</p>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>Artefato</th><th>Índice</th><th class=\"num\">Achados</th>"
                      + "<th class=\"barra-col\"></th></tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var artefato in run.Artifacts.OrderBy(a => a.Compliance.Overall.Percent))
        {
            sb.Append("<tr><td><code>").Append(Escape(artefato.Path)).Append("</code></td>")
              .Append("<td class=\"").Append(FaixaDe(artefato.Compliance.Overall.Percent)).Append("\">")
              .Append(Percent(artefato.Compliance.Overall.Percent)).Append("</td>")
              .Append("<td class=\"num\">").Append(Escape(artefato.Diagnostics.Count.ToString(Br))).Append("</td>")
              .Append("<td class=\"barra-col\">").Append(Barra(artefato.Compliance.Overall.Percent))
              .AppendLine("</td></tr>");
        }

        sb.AppendLine("</tbody></table></section>");
    }

    private static void RenderFindings(StringBuilder sb, AnalysisRun run)
    {
        sb.AppendLine("<section>");
        sb.AppendLine("<h2>Achados</h2>");

        if (run.AllDiagnostics.Count == 0)
        {
            sb.AppendLine("<p class=\"limpo\">Nenhum achado.</p></section>");
            return;
        }

        RenderFilters(sb, run);

        foreach (var artefato in run.Artifacts.Where(a => a.Diagnostics.Count > 0))
        {
            sb.Append("<h3><code>").Append(Escape(artefato.Path)).AppendLine("</code></h3>");

            foreach (var porEntrada in artefato.Diagnostics
                .GroupBy(d => d.Location.EntryPath, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.Append("<h4>").Append(Escape(porEntrada.Key)).AppendLine("</h4>");
                sb.AppendLine("<ul class=\"achados\">");

                foreach (var d in porEntrada.OrderByDescending(x => x.Severity).ThenBy(x => x.RuleId, StringComparer.Ordinal))
                    RenderFinding(sb, d);

                sb.AppendLine("</ul>");
            }
        }

        sb.AppendLine("</section>");
    }

    private static void RenderFilters(StringBuilder sb, AnalysisRun run)
    {
        sb.AppendLine("<div class=\"filtros\">");
        sb.AppendLine("<span class=\"rotulo\">Mostrar:</span>");

        foreach (var severidade in new[] { Severity.Error, Severity.Warning, Severity.Info })
        {
            var quantos = run.AllDiagnostics.Count(d => d.Severity == severidade);
            if (quantos == 0)
                continue;

            var chave = Chave(severidade);

            sb.Append("<label><input type=\"checkbox\" checked data-sev=\"").Append(chave).Append("\"> ")
              .Append(Escape(Rotulo(severidade))).Append(" <span class=\"contagem\">")
              .Append(Escape(quantos.ToString(Br))).AppendLine("</span></label>");
        }

        sb.AppendLine("<input type=\"search\" id=\"busca\" placeholder=\"filtrar por regra, símbolo ou texto\">");
        sb.AppendLine("</div>");
        sb.AppendLine("<p class=\"vazio\" id=\"vazio\" hidden>Nenhum achado corresponde ao filtro.</p>");
    }

    private static void RenderFinding(StringBuilder sb, Diagnostic d)
    {
        var doc = RuleDocs.Find(d.RuleId);
        var chave = Chave(d.Severity);

        sb.Append("<li class=\"achado\" data-sev=\"").Append(chave).Append("\">");
        sb.Append("<span class=\"sev sev-").Append(chave).Append("\">").Append(Escape(Rotulo(d.Severity))).Append("</span>");
        sb.Append("<a class=\"regra\" href=\"").Append(Repository).Append("/blob/main/docs/rules/")
          .Append(Escape(d.RuleId)).Append(".md\" title=\"")
          .Append(Escape(doc?.Title ?? d.RuleId)).Append("\">").Append(Escape(d.RuleId)).Append("</a>");

        if (!string.IsNullOrEmpty(d.Location.Symbol))
            sb.Append("<code class=\"simbolo\">").Append(Escape(d.Location.Symbol!)).Append("</code>");

        sb.Append("<span class=\"mensagem\">").Append(Escape(d.Message)).Append("</span>");
        sb.AppendLine("</li>");
    }

    private static void RenderFooter(StringBuilder sb)
    {
        sb.Append("<footer>Gerado por <a href=\"").Append(Repository).Append("\">pp-lint</a> ")
          .Append(Escape(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
          .AppendLine(". Os identificadores de regra levam à documentação completa.</footer>");
    }

    /// <summary>
    /// A barra existe para comparar linhas de relance. Ela é decorativa e fica
    /// escondida de leitor de tela: o número ao lado já diz a mesma coisa, e
    /// ouvir os dois seria repetição.
    /// </summary>
    private static string Barra(double percent)
    {
        var largura = Math.Clamp(percent, 0, 100).ToString("0.#", CultureInfo.InvariantCulture);

        return $"<div class=\"barra\" aria-hidden=\"true\"><div class=\"preenchida {FaixaDe(percent)}\" "
               + $"style=\"width:{largura}%\"></div></div>";
    }

    /// <summary>
    /// A faixa vira classe CSS, e a cor vem de lá. Três faixas, não um gradiente
    /// contínuo: quem lê precisa de "está bem, está ruim, está grave", e não de
    /// distinguir 87% de 84% pela cor.
    /// </summary>
    private static string FaixaDe(double percent) => percent switch
    {
        >= 95 => "boa",
        >= 80 => "media",
        _ => "ruim",
    };

    private static string Percent(double value) => Escape(value.ToString("0.0", Br) + "%");

    private static string Plural(int quantidade, string singular, string plural) =>
        $"{quantidade.ToString(Br)} {(quantidade == 1 ? singular : plural)}";

    private static string Chave(Severity s) => s switch
    {
        Severity.Error => "erro",
        Severity.Warning => "aviso",
        _ => "info",
    };

    private static string Rotulo(Severity s) => s switch
    {
        Severity.Error => "erro",
        Severity.Warning => "aviso",
        _ => "informação",
    };

    private static string Nome(RuleCategory c) => c switch
    {
        RuleCategory.Naming => "Nomenclatura",
        RuleCategory.PowerFx => "Power Fx",
        RuleCategory.Flow => "Fluxos",
        RuleCategory.Duplication => "Duplicação",
        RuleCategory.Performance => "Desempenho",
        RuleCategory.Security => "Segurança",
        _ => "Solução",
    };

    /// <summary>
    /// As mensagens citam fórmulas do usuário, que contêm &amp;, &lt; e aspas —
    /// a PF118 chega a citar trechos de SVG. Sem escape, o relatório sai
    /// quebrado ou, pior, executa o que veio do artefato.
    /// </summary>
    private static string Escape(string texto) => WebUtility.HtmlEncode(texto);

    private const string Css = """
        :root {
          color-scheme: light dark;
          --fundo: #ffffff;
          --fundo-caixa: #f6f7f9;
          --texto: #1a1d21;
          --texto-fraco: #5b6470;
          --borda: #dfe3e8;
          --boa: #1a7f4b;
          --media: #9a6700;
          --ruim: #b42318;
          --erro-fundo: #fdecea;
          --aviso-fundo: #fff6e0;
          --info-fundo: #eef2f7;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --fundo: #14171a;
            --fundo-caixa: #1c2024;
            --texto: #e7eaee;
            --texto-fraco: #9aa4b0;
            --borda: #2c3238;
            --boa: #4ec98a;
            --media: #e0b341;
            --ruim: #f2776a;
            --erro-fundo: #3a1d1b;
            --aviso-fundo: #33290f;
            --info-fundo: #1f252c;
          }
        }
        * { box-sizing: border-box; }
        body {
          margin: 0 auto; padding: 2rem 1.25rem 4rem; max-width: 60rem;
          background: var(--fundo); color: var(--texto);
          font: 15px/1.55 system-ui, -apple-system, "Segoe UI", sans-serif;
        }
        h1 { font-size: 1.5rem; margin: 0 0 .25rem; }
        h2 { font-size: 1.1rem; margin: 2.5rem 0 .75rem; }
        h3 { font-size: .95rem; margin: 1.75rem 0 .5rem; }
        h4 { font-size: .85rem; margin: 1rem 0 .35rem; color: var(--texto-fraco); font-weight: 600; }
        code { font-family: ui-monospace, "Cascadia Mono", Menlo, Consolas, monospace; font-size: .875em; }
        a { color: inherit; }
        .resumo { margin: 0; color: var(--texto-fraco); }
        .nota { color: var(--texto-fraco); font-size: .85rem; max-width: 46rem; }
        .indice {
          margin-top: 1.75rem; padding: 1.25rem 1.5rem;
          background: var(--fundo-caixa); border: 1px solid var(--borda); border-radius: 10px;
        }
        .indice p { margin: .35rem 0 0; }
        .numero { font-size: 2.75rem; font-weight: 650; line-height: 1; letter-spacing: -.02em; }
        .boa { color: var(--boa); }
        .media { color: var(--media); }
        .ruim { color: var(--ruim); }
        table { width: 100%; border-collapse: collapse; }
        th, td { text-align: left; padding: .5rem .6rem; border-bottom: 1px solid var(--borda); }
        th { font-size: .8rem; text-transform: uppercase; letter-spacing: .04em; color: var(--texto-fraco); font-weight: 600; }
        td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
        .barra-col { width: 34%; }
        .barra { height: 7px; border-radius: 4px; background: var(--borda); overflow: hidden; }
        .preenchida { height: 100%; border-radius: 4px; background: currentColor; }
        .filtros {
          display: flex; flex-wrap: wrap; gap: .85rem; align-items: center;
          padding: .7rem .9rem; margin-bottom: .5rem;
          background: var(--fundo-caixa); border: 1px solid var(--borda); border-radius: 8px;
        }
        .filtros .rotulo { font-size: .8rem; color: var(--texto-fraco); text-transform: uppercase; letter-spacing: .04em; }
        .filtros label { display: inline-flex; align-items: center; gap: .3rem; cursor: pointer; font-size: .9rem; }
        .contagem { color: var(--texto-fraco); font-variant-numeric: tabular-nums; }
        #busca {
          flex: 1 1 14rem; min-width: 10rem; padding: .35rem .55rem;
          border: 1px solid var(--borda); border-radius: 6px;
          background: var(--fundo); color: inherit; font: inherit; font-size: .9rem;
        }
        .achados { list-style: none; margin: 0; padding: 0; }
        .achado {
          display: grid; gap: .1rem .55rem; align-items: baseline;
          grid-template-columns: auto auto 1fr;
          padding: .45rem .6rem; border-bottom: 1px solid var(--borda);
        }
        .achado .mensagem { grid-column: 1 / -1; color: var(--texto-fraco); font-size: .9rem; }
        .sev {
          font-size: .72rem; text-transform: uppercase; letter-spacing: .05em; font-weight: 650;
          padding: .12rem .4rem; border-radius: 4px; white-space: nowrap;
        }
        .sev-erro { color: var(--ruim); background: var(--erro-fundo); }
        .sev-aviso { color: var(--media); background: var(--aviso-fundo); }
        .sev-info { color: var(--texto-fraco); background: var(--info-fundo); }
        .regra { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: .85rem; font-weight: 600; }
        .simbolo { color: var(--texto); }
        .limpo { color: var(--boa); font-weight: 600; }
        .vazio { color: var(--texto-fraco); font-style: italic; }
        footer { margin-top: 3rem; padding-top: 1rem; border-top: 1px solid var(--borda); color: var(--texto-fraco); font-size: .85rem; }
        @media print {
          .filtros { display: none; }
          .achado { break-inside: avoid; }
        }
        """;

    /// <summary>
    /// O filtro é a única razão de o relatório ser HTML e não texto: com
    /// trezentos achados, poder isolar os erros ou procurar um controle pelo
    /// nome é o que torna a lista utilizável. Sem script, o arquivo continua
    /// legível — só perde o filtro.
    /// </summary>
    private const string Script = """
        (function () {
          var achados = Array.prototype.slice.call(document.querySelectorAll('.achado'));
          var caixas = Array.prototype.slice.call(document.querySelectorAll('.filtros input[data-sev]'));
          var busca = document.getElementById('busca');
          var vazio = document.getElementById('vazio');
          if (!achados.length) return;

          function aplicar() {
            var ligadas = {};
            caixas.forEach(function (c) { ligadas[c.getAttribute('data-sev')] = c.checked; });
            var termo = (busca && busca.value || '').trim().toLowerCase();
            var visiveis = 0;

            achados.forEach(function (li) {
              var sev = li.getAttribute('data-sev');
              var passaSev = ligadas[sev] !== false;
              var passaTermo = !termo || li.textContent.toLowerCase().indexOf(termo) !== -1;
              var mostra = passaSev && passaTermo;
              li.hidden = !mostra;
              if (mostra) visiveis++;
            });

            // Um título de arquivo sem nenhum achado visível vira ruído.
            Array.prototype.forEach.call(document.querySelectorAll('.achados'), function (ul) {
              var algum = ul.querySelector('.achado:not([hidden])');
              ul.hidden = !algum;
              var titulo = ul.previousElementSibling;
              if (titulo && titulo.tagName === 'H4') titulo.hidden = !algum;
            });

            Array.prototype.forEach.call(document.querySelectorAll('h3'), function (h3) {
              var proximo = h3.nextElementSibling, algum = false;
              while (proximo && proximo.tagName !== 'H3') {
                if (proximo.querySelector && proximo.querySelector('.achado:not([hidden])')) { algum = true; break; }
                proximo = proximo.nextElementSibling;
              }
              h3.hidden = !algum;
            });

            if (vazio) vazio.hidden = visiveis !== 0;
          }

          caixas.forEach(function (c) { c.addEventListener('change', aplicar); });
          if (busca) busca.addEventListener('input', aplicar);
        })();
        """;
}
