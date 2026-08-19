# pp-lint — Progresso da Fase 1

Plano: `docs/superpowers/plans/2026-08-18-pp-lint-fase-1.md`
Spec: `docs/superpowers/specs/2026-08-18-pp-lint-design.md`

**Fase 1 concluída** — 17 de 17 tasks, 173 testes verdes (170 aprovados + 3 pulados
por falta do fixture real).

| # | Task | Status | Testes |
|---|------|--------|--------|
| 1 | Scaffolding da solução e tipos de diagnóstico | ✅ | 6 |
| 2 | Parser de argumentos do CLI | ✅ | 9 |
| 3 | Leitura de artefatos (zip e pasta) | ✅ | 9 |
| 4 | Modelo de domínio (IR) | ✅ | 5 |
| 5 | MsappExtractor | ✅ | 9 |
| 6 | FlowExtractor | ✅ | 11 |
| 7 | SolutionExtractor e ProjectLoader | ✅ | 11 |
| 8 | Parser de Power Fx e percurso de AST | ✅ | 11 |
| 9 | Motor de regras | ✅ | 9 |
| 10 | Índice de conformidade | ✅ | 9 |
| 11 | Regras NM010 e NM011 (nomenclatura de controles) | ✅ | 16 |
| 12 | Grafo de variáveis e regra PF101 | ✅ | 16 |
| 13 | Regra PF110 (condição constante) | ✅ | 10 |
| 14 | Regra FL201 (variável de fluxo não usada) | ✅ | 10 |
| 15 | Relatório de terminal e comando check | ✅ | 10 |
| 16 | Robustez, teste ponta a ponta e contrato do catálogo | ✅ | 19 |
| 17 | Validação contra artefato real | ⏸ | 3 pulados |

Total por projeto: Core 29 · PowerFx 21 · Extractors 40 · Rules 50 · Cli 30.

## Desvios do plano decididos durante a execução

1. **`net10.0` em vez de `net8.0`.** Os templates do SDK 10 instalado não oferecem
   `net8.0`, e o .NET 8 sai de suporte em novembro de 2026. O .NET 10 é o LTS atual.
2. **`nuget.org` registrado como fonte.** A máquina não tinha nenhuma fonte NuGet
   configurada. Reverter com `dotnet nuget remove source nuget.org`.
3. **Sem `InvariantGlobalization`.** O relatório formata percentuais em pt-BR
   (vírgula decimal); a globalização invariante quebraria isso silenciosamente.
4. **Parser do Power Fx com cultura invariante e `AllowsSideEffects`.** Descoberto
   no spike da Task 8: sem isso, em máquina pt-BR o separador de argumentos vira `;`
   e praticamente toda fórmula do `.msapp` (que guarda `InvariantScript`, com vírgula)
   seria reportada como erro de sintaxe. `AllowsSideEffects` habilita o encadeamento
   com `;` das propriedades de comportamento.
5. **`Console.OutputEncoding = UTF8`.** Sem isso a acentuação das mensagens em
   português sai corrompida no console do Windows — só aparece no binário publicado,
   nunca nos testes.
6. **`PublishTrimmed` desativado.** O motor descobre regras por reflexão e o trimmer
   não consegue provar que os tipos sobrevivem (IL2026/IL2067), quebrando a compilação.
   A compressão single-file reduz o binário de 83 MB para 40 MB. Reduzir mais exigiria
   trocar a descoberta por um registro explícito de regras — previsto para a fase de release.
7. **`CallNode.Args.ChildNodes`**, não `.Children`, no pacote `Microsoft.PowerFx.Core` 1.8.1.

## Correções de testes durante a execução

- `CorruptZip`: o teste esperava "inválido", a mensagem diz "não é um pacote zip válido".
- `Check_Quiet`: o teste exigia ausência do ID da regra, mas `--quiet` mantém o resumo
  com as ocorrências por regra — passou a verificar a ausência do achado detalhado.

## Pendência que depende de você

A **Task 17** precisa de uma solução `.zip` exportada real, com dados anonimizados,
em `tests/fixtures/solucao-exemplo.zip`. Os três testes ficam pulados até lá.

Até que isso rode, os extractors estão validados apenas contra artefatos sintéticos
construídos a partir da documentação do formato. É o maior risco remanescente da
fase: se a estrutura de um `.msapp` real divergir do esperado, o linter não acha nada.
