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

## Próximas fases

- **2b** — catálogo NM e PF completo, grafo de variáveis de contexto e coleções.
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
