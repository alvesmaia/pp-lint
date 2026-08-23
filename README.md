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

## Regras

| Id | Regra | Severidade |
|---|---|---|
| NM001 | Variável global fora da convenção (`varTotal`) | Aviso |
| NM002 | Variável de contexto fora da convenção (`locFiltro`) | Aviso |
| NM003 | Coleção fora da convenção (`colItens`) | Aviso |
| NM010 | Controle com nome padrão do Studio (`Button1`, `Screen1`) | Erro |
| NM011 | Prefixo do controle não corresponde ao tipo, inclusive prefixo de **outro** tipo | Aviso |
| NM020 | Coluna com prefixo de outro publisher que não o da solução | Erro |
| NM021 | Nome de esquema da coluna fora de PascalCase | Aviso |
| NM023 | Nome de exibição divergente do nome de esquema | Informação |
| NM040 | Nome com acento, cedilha, espaço ou caractere especial — variáveis, controles, telas, **colunas**, tabelas, fluxos e ações | **Erro** |
| PF101 | Variável global definida e nunca lida | Aviso |
| PF102 | Variável de contexto definida e nunca lida **na tela dela** | Aviso |
| PF103 | Coleção criada e nunca usada | Aviso |
| PF104 | Nome lido mas nunca definido — quase sempre erro de digitação | Erro |
| PF105 | Variável global lida em uma única tela; caberia contexto | Informação |
| PF106 | Mesmo `Set` com o mesmo valor duas vezes na mesma fórmula | Aviso |
| PF110 | Condição constante (`If(2 > 1, …)`, `If(true, …)`) | Erro |
| PF111 | `If(cond, true, false)` — o If não acrescenta nada | Aviso |
| PF112 | Comparação com booleano (`x = true`) | Aviso |
| PF113 | Os dois ramos do `If` são idênticos | Erro |
| PF114 | Condição repetida no ramo `senão` — ramo inalcançável | Erro |
| PF115 | Dupla negação (`Not(Not(x))`, `!!x`) | Aviso |
| PF116 | `Filter(fonte, true)` — filtro sem efeito | Aviso |
| PF117 | `CountRows(x) > 0` em vez de `!IsEmpty(x)` | Aviso |
| PF118 | Concatenação com texto vazio | Informação |
| PF120 | `UpdateContext` no `App.OnStart`, que não define nada | **Erro** |
| PF121 | Referência a controle de outra tela | Aviso |
| PF122 | `ForAll` que escreve item a item | Aviso |
| PF123 | Expressão aninhada acima do limite | Aviso |
| PF124 | Mesma cor literal em cinco ou mais lugares | Informação |
| PF125 | Texto literal num app que já traduz | Informação |
| PF130 | A fórmula não compila | **Erro** |
| FL201 | Variável de fluxo inicializada e nunca lida | Aviso |
| FL202 | Variável usada antes de ser inicializada | Erro |
| FL203 | Saída de ação computacional nunca consumida | Aviso |
| FL210 | Fluxo sem nenhum tratamento de falha | Erro |
| FL222 | `Aplicar a cada` dentro de outro | Aviso |
| FL230 | Recorrência mais frequente que o limiar (15 min) | Aviso |
| FL240 | Fluxo sem descrição | Informação |
| FL241 | `Executar após` aponta para ação inexistente | Erro |
| DUP301 | Fórmula idêntica repetida três ou mais vezes | Aviso |
| DUP303 | Controle invisível que nenhuma fórmula referencia | Aviso |
| DUP304 | Tela para a qual nenhum `Navigate` aponta | Aviso |
| DUP305 | Fonte de dados declarada e nunca consultada | Aviso |
| PERF401 | Função não delegável sobre fonte de dados externa | **Erro** |
| PERF402 | Chamadas de rede em sequência no `OnStart` | Aviso |
| PERF403 | `OnStart` acima do orçamento de operações | Aviso |
| SEC501 | Segredo, chave ou token escrito à mão | **Erro** |
| SEC502 | Endereço de ambiente fixo na fórmula | Aviso |
| SEC503 | Conector que envia dados para fora da organização | Aviso |

`pp-lint rules` lista o catálogo instalado.

`pp-lint explain <ID>` imprime a documentação completa de uma regra — o que ela pega, por
que importa e um exemplo ruim e um bom. Os mesmos textos estão em [`docs/rules/`](docs/rules/).

## Formatos de saída

| Formato | Para quê |
|---|---|
| `text` | leitura no terminal, com índice de conformidade (padrão) |
| `json` | automação; o esquema é versionado no campo `schemaVersion` |
| `sarif` | anotação inline no pull request e aba Security do GitHub |

Com vários artefatos na mesma execução, o relatório de texto mostra o índice de cada um
antes do geral.

Exemplo de uso em GitHub Actions:

```yaml
- run: pp-lint check solucao.zip --format sarif --output pp-lint.sarif
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: pp-lint.sarif
```

O SARIF aponta o achado para o arquivo do artefato, sem número de linha: o `.msapp` guarda
as fórmulas dentro de JSON gerado pelo Studio, e o extractor ainda não registra o offset.
A entrada do pacote e o nome do controle viajam em `logicalLocations`.

### Como o pp-lint entende variáveis

Variáveis de contexto pertencem a uma tela — é assim que o Power Fx funciona.
Uma `locFiltro` criada em `scrPedidos` e referenciada em `scrDetalhe` não conta
como uso: lá é outra variável. `Navigate(scrDestino, Fade, {locId: 7})` cria a
variável na tela **de destino**, e é lá que o pp-lint procura leituras.

Globais e coleções valem no app inteiro.

Para decidir se um identificador é variável, o pp-lint descarta primeiro o que tem
dono conhecido: controles, telas, fontes de dados, funções e enums do Power Fx, e
escopos de linha (`ThisItem`, `Self`, `Parent`, apelidos de `As`, campos de
`With`). É isso que permite a PF104 apontar erro de digitação sem acusar cada
galeria do app.

A PF104 é deliberadamente conservadora, porque tem severidade `Error`. Ela não
reporta nomes dentro de funções de tabela (`Filter`, `Sort`, `LookUp`), onde o
identificador pode ser uma coluna do registro; nem nomes à esquerda de um ponto
(`TraceSeverity.Warning`), que são enums e objetos do host; nem nomes que o app
usa como fonte de dados (`Refresh(GameServer)`), ainda que não estejam declarados
nos metadados. Contra um app real de 827 fórmulas, ela não produz nenhum falso
positivo — e há um teste que falha se isso mudar.

O tipo do controle vem do `Template.Name` gravado pelo próprio Studio, nunca do nome —
por isso a NM011 distingue duas situações que têm causas diferentes:

```
'SalvarPedido' é do tipo 'button' e deveria começar com o prefixo 'btn'.
'btnTeste' usa o prefixo 'btn', que sugere 'button', mas o controle é do
tipo 'toggleSwitch'. Use o prefixo 'tgl'.
```

A segunda é a mais séria: um prefixo de outro tipo costuma ser copiar-colar de um
controle seguido de troca de tipo sem renomear, e faz quem lê a fórmula depois
acreditar num tipo que não existe mais.

## Configuração

O pp-lint procura `pp-lint.toml` no diretório atual e nos ancestrais. Sem arquivo,
usa o preset `camel-prefix` e avisa uma vez qual está em uso. Veja
`pp-lint.example.toml` para um arquivo comentado.

```toml
[pp-lint]
preset = "pascal-type"
ignore = ["NM011"]
fail-on = "warning"
```

**Presets de nomenclatura.** Impor uma régua única faz o linter reclamar de apps
bem escritos que apenas seguem outro padrão. Escolha o que corresponde à sua
convenção:

| Preset | Controles | Variáveis |
|---|---|---|
| `camel-prefix` | `btnSalvar`, `lblTitulo` | `varTotal` |
| `pascal-type` | `ButtonSalvar`, `LabelTitulo` | `VarTotal` |

O efeito é grande. No app real usado para validar o projeto, que nomeia como
`ButtonCreateGame`:

| | camel-prefix | pascal-type |
|---|---|---|
| Avisos de prefixo | 129 | 19 |
| Conformidade de nomenclatura | 77,9% | 95,4% |
| Erros reais encontrados | 3 | 3 |

Os 3 erros sobrevivem à troca — o que some é o ruído de comparar o app com uma
convenção que ele nunca adotou. Os 19 avisos restantes são inconsistências
verdadeiras do próprio app (`LblAppName1` onde todo o resto usa `Label...`).

Precedência: preset → `pp-lint.toml` → flags de linha de comando.

Também dá para escolher regras sem arquivo nenhum:

```bash
pp-lint check App.msapp --select NM --ignore NM011
pp-lint check App.msapp --config ../equipe/pp-lint.toml
```

`--select` e `--ignore` aceitam ID (`PF101`) ou categoria inteira (`PF`), separados
por vírgula. `--ignore` sempre vence `--select`.

Uma lista passada na linha de comando **substitui** a do arquivo, não soma. Se o
`pp-lint.toml` tem `ignore = ["PF125"]` e você roda `--ignore NM011`, apenas NM011
é ignorada naquela execução — PF125 volta a valer. É deliberado (a flag diz
exatamente o que você quer naquela vez), mas surpreende se você esperava união.

## Silenciar um achado

Em Power Fx, um comentário na própria fórmula:

```powerapps
// pp-lint: disable=PF101
Set(varTemporaria, 1)
```

Em fluxos, JSON não aceita comentário — use o campo **descrição** da ação:

```
pp-lint: disable=FL201
```

A diretiva vale para o controle ou a ação onde aparece, e aceita vários IDs
separados por vírgula.

**Suprimir tira o achado do relatório, não o débito da nota.** O item continua
contando como violação no índice de conformidade — silenciar tudo mostra 0%, não
100%. Isso é deliberado: se o alvo saísse da conta, `(v−1)/(t−1)` seria sempre
maior que `v/t` e a nota subiria a cada supressão, tornando a métrica inútil.

Para quando a regra genuinamente não se aplica — e não deve pesar na nota — use
`ignore` (regra inteira) ou `per-artifact-ignores` (por artefato).

Para desligar uma regra inteira, use `ignore` no TOML ou `--ignore NM011`. Para
excluir artefatos específicos:

```toml
[pp-lint.per-artifact-ignores]
"**/Legado*.msapp" = ["NM010", "NM011"]
```

**Limitação atual:** o glob é comparado com o caminho que você passou na linha de
comando, não com cada arquivo analisado. Ele funciona ao apontar o `.msapp`
diretamente (`pp-lint check apps/LegadoVendas.msapp`), mas **não** filtra apps
individuais dentro de uma solução `.zip` nem ao rodar sobre um diretório — nesses
casos o caminho comparado é o do `.zip` ou da pasta. Para excluir um app dentro de
uma solução, use supressão inline por enquanto. Filtro por artefato analisado está
previsto para a Fase 2c.

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
