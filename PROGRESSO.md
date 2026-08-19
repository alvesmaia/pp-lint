# pp-lint — Progresso da Fase 1

Plano: `docs/superpowers/plans/2026-08-18-pp-lint-fase-1.md`
Spec: `docs/superpowers/specs/2026-08-18-pp-lint-design.md`

Legenda: ✅ concluída · 🔄 em andamento · ⬜ pendente

| # | Task | Status | Testes |
|---|------|--------|--------|
| 1 | Scaffolding da solução e tipos de diagnóstico | ✅ | 6 |
| 2 | Parser de argumentos do CLI | ✅ | 9 |
| 3 | Leitura de artefatos (zip e pasta) | ✅ | 9 |
| 4 | Modelo de domínio (IR) | ✅ | 5 |
| 5 | MsappExtractor | ✅ | 9 |
| 6 | FlowExtractor | ✅ | 11 |
| 7 | SolutionExtractor e ProjectLoader | ✅ | 11 |
| 8 | Parser de Power Fx e percurso de AST | 🔄 | 11 |
| 9 | Motor de regras | ⬜ | — |
| 10 | Índice de conformidade | ⬜ | — |
| 11 | Regras NM010 e NM011 (nomenclatura de controles) | ⬜ | — |
| 12 | Grafo de variáveis e regra PF101 | ⬜ | — |
| 13 | Regra PF110 (condição constante) | ⬜ | — |
| 14 | Regra FL201 (variável de fluxo não usada) | ⬜ | — |
| 15 | Relatório de terminal e comando check | ⬜ | — |
| 16 | Robustez, teste ponta a ponta e contrato do catálogo | ⬜ | — |
| 17 | Validação contra artefato real | ⬜ | — |

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

## Pendência que depende de você

A **Task 17** precisa de uma solução `.zip` exportada real, com dados anonimizados,
colocada em `tests/fixtures/solucao-exemplo.zip`. Até lá, os extractors estão
validados apenas contra artefatos sintéticos construídos a partir da documentação
do formato.
