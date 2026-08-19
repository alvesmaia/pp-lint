# pp-lint — Progresso

Spec: `docs/superpowers/specs/2026-08-18-pp-lint-design.md`

## Fase 1 — concluída

17 tasks. Núcleo do linter: IR, extractors, motor de regras, índice de
conformidade, relatório de terminal e 5 regras piloto. Validada contra 4 canvas
apps reais da Microsoft, o que revelou dois defeitos que os testes sintéticos não
pegavam (controles gerados pelo Studio e caminho das data sources).

Plano: `docs/superpowers/plans/2026-08-18-pp-lint-fase-1.md`.

## Fase 2a — concluída

Configuração e supressão. 10 tasks.

| # | Task | Status |
|---|------|--------|
| 1 | Descrição da ação de fluxo | ✅ |
| 2 | Índice de supressões | ✅ |
| 3 | Motor aplica supressão | ✅ |
| 4 | Presets de nomenclatura | ✅ |
| 5 | Leitura do arquivo TOML | ✅ |
| 6 | Resolver de configuração | ✅ |
| 7 | Seleção de regras e ignores por artefato | ✅ |
| 8 | Descoberta do arquivo de configuração | ✅ |
| 9 | Integração no CLI | ✅ |
| 10 | Documentação e validação contra os apps reais | ✅ |

Plano: `docs/superpowers/plans/2026-08-19-pp-lint-fase-2a.md`.

### Resultado medido no app real

O `chess-real.msapp` nomeia controles como `ButtonCreateGame`. Trocar o preset
para o que corresponde à convenção dele:

| | camel-prefix | pascal-type |
|---|---|---|
| Avisos NM011 | 129 | 19 |
| Conformidade de nomenclatura | 77,9% | 95,4% |
| Conformidade geral | 95,7% | 99,0% |
| Erros reais (NM010) | 3 | 3 |

Os erros sobrevivem, o ruído some. Os 19 avisos restantes são inconsistências
reais do próprio app — `LblAppName1` onde todo o resto usa `Label...`.

### Desvio de API encontrado na execução

O plano supunha `Toml.ToModel`; o Tomlyn 2.10.1 usa
`TomlSerializer.Deserialize<TomlTable>` e lança `TomlException`. O plano foi
corrigido para refletir a API real.

## Fase 2b-1 — concluída

Variáveis: resolvedor de símbolos, grafo com tipo e escopo, 8 regras novas
(NM001–NM003, PF102–PF106). O catálogo passou de 5 para 13 regras. Plano:
`docs/superpowers/plans/2026-08-19-pp-lint-fase-2b1.md`.

A PF104 foi calibrada contra o `chess-real.msapp` — 827 fórmulas reais — porque
tem severidade Error e um falso positivo quebraria o build de quem confia na
ferramenta. A calibragem reprovou três vezes antes de passar, e cada reprovação
apontou um buraco real no resolvedor:

1. Nomes de coluna dentro de funções de tabela (`Filter(Pedidos, Title = "x")`).
2. Enums e objetos de host à esquerda de um ponto (`TraceSeverity.Warning`).
3. Fontes de dados que o app usa mas não declara nos metadados — `Refresh(GameServer)`
   prova o que `GameServer` é.

Resultado final: zero falsos positivos no app real.

Descoberta da execução: `Engine.GetAllFunctionNames()` não devolve as funções de
comportamento do Power Apps (`Set`, `Notify`, `Navigate`, `Collect`) — quem as
registra é o host, não o engine core. Entram por lista complementar.

## Fase 2b-2 — concluída

Lógica redundante: comparador estrutural de AST e 8 regras (PF111–PF118).
O catálogo passou de 13 para 21 regras. Plano:
`docs/superpowers/plans/2026-08-19-pp-lint-fase-2b2.md`.

Contra o app real, as oito regras juntas produzem 3 achados em 827 fórmulas —
todos verificados à mão e legítimos: um `If(ThisItem.Id = 4, true, false)` e duas
concatenações com texto vazio. O comparador estrutural criado aqui será a base da
detecção de duplicação (DUP301).

Descoberta da execução: o parser representa números escritos na fórmula como
`DecLitNode`, não `NumLitNode` — aceitar só um dos dois faria a PF117 nunca
disparar.

## Próximas fases

- **2b-3** — telas, componentes, fluxos, colunas (NM012–NM041, PF120–PF130).
- **2c** — formatos JSON e SARIF, `explain` com documentação, `inspect`, índice
  por artefato.
- **Release** — baseline, benchmark, binários públicos.

## Pendência que depende do dono do projeto

Uma solução `.zip` exportada real em `tests/fixtures/solucao-exemplo.zip`. O caminho
de solução exportada — `solution.xml`, tabelas Dataverse, cloud flows — segue
validado apenas contra fixtures sintéticos, e todo o catálogo `FL*` da Fase 2b
depende dele. Dois testes ficam pulados até lá.

## Decisões de ambiente

1. **`net10.0`**, não `net8.0`: os templates do SDK instalado não oferecem net8.0,
   e o .NET 8 sai de suporte em novembro de 2026.
2. **`nuget.org` registrado** na máquina, que não tinha fonte NuGet configurada.
   Reverter com `dotnet nuget remove source nuget.org`.
3. **Sem `InvariantGlobalization`**: o relatório formata percentuais em pt-BR.
4. **`Console.OutputEncoding = UTF8`**: sem isso a acentuação sai corrompida no
   console do Windows — só aparece no binário publicado.
5. **`PublishTrimmed` desativado**: o motor descobre regras por reflexão e o
   trimmer quebra a compilação. A compressão reduz o binário de 83 MB para 40 MB.
6. **Não crie `pp-lint.toml` na raiz deste repositório** — só o
   `pp-lint.example.toml`. Um teste de integração verifica o comportamento na
   ausência de configuração e falharia sem nada ter quebrado no produto.

## Fase FL — concluída

Sete regras de Power Automate (FL202, FL203, FL210, FL222, FL230, FL240, FL241) e o
grafo de execução derivado de `runAfter`. O catálogo passou de 21 para 28 regras.
Plano: `docs/superpowers/plans/2026-08-19-pp-lint-fase-fl.md`.

Primeira fase desenhada contra um artefato real desde o início, e a primeira em que o
artefato real não encontrou defeito nenhum nas regras: os dois achados no fluxo do
fixture (sem tratamento de falha, sem descrição) e os cinco silêncios foram conferidos
linha a linha no JSON.

## Fase 2c — concluída

Formatos `json` e `sarif`, comando `explain` com a documentação das 28 regras embarcada no
binário, e índice de conformidade por artefato. `html` e `md` saíram do `--help`: voltam na
Fase 5, junto com o relatório HTML que o spec prevê. O CLI passou a prometer apenas o que
cumpre — antes anunciava cinco formatos e não entregava nenhum.

Duas decisões que o formato impôs:

O SARIF omite `region` quando não há número de linha, em vez de apontar para a linha 1. O
`.msapp` guarda as fórmulas dentro de JSON gerado pelo Studio e o extractor não registra
offset; a anotação aponta para o arquivo, com entrada e símbolo em `logicalLocations`.

Os dois relatórios usam `UnsafeRelaxedJsonEscaping`. O encoder padrão escapa apóstrofo e
sinais de menor como sequências unicode — e as mensagens citam nomes entre apóstrofos o
tempo todo, o que tornaria o arquivo ilegível para quem o abre.

Plano: `docs/superpowers/plans/2026-08-19-pp-lint-fase-2c.md`.

## NM040 — caracteres especiais em nomes

Nome de variável, coleção, controle, tela, fluxo, ação ou variável de fluxo que use algo
fora de letra ASCII, dígito e underscore passa a ser **Erro**. O spec previa Aviso; a
severidade foi elevada a pedido do usuário, e a elevação se justifica: não é estilo.

Em Power Fx o identificador passa a exigir aspas simples em toda referência, e um apóstrofo
mal fechado numa fórmula distante quebra outra coisa. Em coluna de SharePoint o nome
interno vira `Descri_x00e7__x00e3_o`, e é esse o nome que fórmulas e fluxos precisam usar.

Os dois artefatos reais confirmam o problema: o app de xadrez tem onze telas com espaço no
nome e **toda** navegação escreve `Navigate('Game Screen', …)`; o fluxo da PnP tem uma ação
chamada `List_to-do's_by_folder_(V2)`, e a expressão que a consome precisa duplicar o
apóstrofo — `body('List_to-do''s_by_folder_(V2)')`.

Nome de exibição não é cobrado: o texto que o usuário lê continua acentuado. O que a regra
governa é o identificador.
