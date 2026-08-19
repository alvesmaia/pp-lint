# pp-lint

Linter estático para artefatos do Power Platform, inspirado no [ruff](https://docs.astral.sh/ruff/):
um binário único, sem dependência de runtime, que analisa uma solução exportada e emite
um relatório de conformidade.

> **Estado atual: Fase 1.** O núcleo está funcionando de ponta a ponta com 5 regras
> piloto. O catálogo completo (~70 regras) chega nas fases seguintes — veja
> `docs/superpowers/specs/2026-08-18-pp-lint-design.md`.

## O que ele analisa

- **Power Fx** de canvas apps — expressões de controles, `App.OnStart`, componentes.
- **Power Automate Cloud** — definição dos fluxos (schema Logic Apps).
- **Nomenclatura** — controles, telas, componentes, variáveis, coleções, fluxos e
  colunas de dados (Dataverse e SharePoint).

Detecta as mesmas classes de problema que o ruff detecta em Python: símbolos não
utilizados, desvio de convenção de nomes, lógica constante ou redundante, código
morto e duplicação.

## Uso

```bash
pp-lint check MinhaSolucao.zip
pp-lint check AppVendas.msapp --fail-on warning
pp-lint check ./repo-descompactado --quiet
pp-lint rules
```

Aceita solução exportada (`.zip`), canvas app isolado (`.msapp`) ou uma pasta
descompactada — útil para rodar sobre o repositório em CI.

### Saída

```
MinhaSolucao.zip
  Controls/1.json
    Button1
      error   NM010  O controle 'Button1' mantém o nome padrão gerado pelo Studio.
    SalvarPedido.OnSelect
      error   PF110  A condição '2 > 1' tem resultado constante.

  Conformidade geral: 39,0%
    Nomenclatura    50,0%  (3/6 conformes)
    Power Fx         0,0%  (0/2 conformes)

  Resumo: 3 erros, 2 avisos, 0 informações em 0,2 s
```

O **índice de conformidade** é a proporção de checagens aprovadas sobre checagens
realizadas, ponderada por severidade (erro 10, aviso 3, informação 1). Cada regra
declara quantos alvos examinou, então o número é sempre "X% dos itens verificados
estão conformes" — auditável a partir das contagens exibidas e comparável entre
apps de tamanhos diferentes.

### Códigos de saída

| Código | Significado |
|---|---|
| 0 | Nenhum achado no nível de `--fail-on` |
| 1 | Achados no nível de `--fail-on` ou acima |
| 2 | Erro de execução (artefato inválido, opção desconhecida) |

## Regras da Fase 1

| Id | Regra | Severidade |
|---|---|---|
| NM010 | Controle com nome padrão do Studio (`Button1`, `Screen1`) | Erro |
| NM011 | Prefixo do controle não corresponde ao tipo (`btn`, `lbl`, …) | Aviso |
| PF101 | Variável global definida e nunca lida | Aviso |
| PF110 | Condição constante (`If(2 > 1, …)`, `If(true, …)`) | Erro |
| FL201 | Variável de fluxo inicializada e nunca lida | Aviso |

`pp-lint rules` lista o catálogo instalado.

## Garantias

- **Somente leitura.** O linter nunca escreve no artefato analisado. Não existe
  `--fix`: o produto é o relatório.
- **Offline.** Nenhuma credencial, nenhuma chamada de rede. Tudo é extraído do
  próprio arquivo.
- **Sem dependências externas.** Não precisa do `pac CLI` nem do .NET instalado —
  o `.msapp` é lido diretamente.

## Build

```bash
dotnet test
dotnet publish src/PpLint.Cli -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

Runtimes suportados: `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`.

**Sobre o tamanho do binário (~40 MB):** `PublishTrimmed` está desativado porque o
motor descobre as regras por reflexão, e o trimmer não consegue provar que os tipos
sobrevivem (IL2026/IL2067). Reduzir isso — provavelmente trocando a descoberta por
um registro explícito de regras, o que também abriria caminho para AOT — está
previsto para a fase de release.

## Arquitetura

```
ArchiveReader → Extractors → PowerPlatformProject (IR) → RuleEngine
             → Diagnostics → ComplianceScorer → Reporter
```

As regras enxergam apenas o IR, nunca o formato de origem: suportar um novo tipo de
artefato não toca em nenhuma regra. Análise de Power Fx roda sobre a AST real do
`Microsoft.PowerFx.Core`, não sobre texto.

| Projeto | Responsabilidade |
|---|---|
| `PpLint.Core` | IR, diagnósticos, motor de regras, índice de conformidade |
| `PpLint.Extractors` | Leitura de `.zip`/`.msapp`/pasta e extração para o IR |
| `PpLint.PowerFx` | Parser de Power Fx, percurso de AST, grafo de variáveis |
| `PpLint.Rules` | Uma classe por regra |
| `PpLint.Cli` | Linha de comando e renderização do relatório |

## Contribuindo com uma regra

Uma regra é uma classe com `[Rule]` e um método `Check`:

```csharp
[Rule("PF101", RuleCategory.PowerFx, Severity.Warning)]
public sealed class UnusedGlobalVariableRule : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var app in ctx.Project.Apps)
        {
            var graph = VariableGraph.Build(app);
            foreach (var variable in graph.Globals)
            {
                ctx.Evaluated(1);                       // denominador do índice
                if (!graph.IsRead(variable.Name))
                    ctx.Report(variable.Location, "…");  // numerador
            }
        }
    }
}
```

Duas obrigações, verificadas por testes de contrato que quebram o build:

1. Chamar `ctx.Evaluated` para **todo** alvo examinado, inclusive quando não há
   violação — sem isso o índice fica incorreto.
2. Emitir **no máximo uma violação por alvo avaliado**, para que a conformidade
   permaneça em `[0, 100]`.

Toda regra precisa de um teste positivo e um negativo. Falso positivo destrói a
confiança no linter mais rápido que uma regra faltando.

## Licença

MIT.
