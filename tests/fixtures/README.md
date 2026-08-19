# Fixtures de artefatos reais

Os testes unitários usam JSONs sintéticos, construídos a partir da documentação
do formato. Eles provam que o código faz o que projetamos — não que um artefato
real é como projetamos. Estes fixtures fecham essa lacuna.

## `chess-real.msapp` (versionado)

Canvas app real gerado pelo Power Apps Studio, com 301 controles e 827 expressões
Power Fx. Vem de `src/PAModelTests/Apps/Chess_for_Power_Apps_v1.03.msapp` do
repositório [microsoft/PowerApps-Tooling](https://github.com/microsoft/PowerApps-Tooling),
licença MIT — a ferramenta oficial de pack/unpack da Microsoft, que o usa como
fixture dos próprios testes.

Foi ele que revelou dois defeitos que os testes sintéticos não pegavam:

- as data sources ficam em `References/DataSources.json`, não em `DataSources/DataSources.json`;
- `galleryTemplate*` e `DataCard*` são gerados pelo Studio e não devem ser cobrados
  por convenção de nome (16 falsos positivos num app só).

## `solucao-exemplo.zip` (versionado)

Solução exportada real, com um cloud flow de 12 ações. Vem de
`samples/ai-time-management-flow` do repositório
[pnp/powerplatform-samples](https://github.com/pnp/powerplatform-samples),
licença MIT.

Valida o caminho que os fixtures sintéticos não cobriam: `solution.xml`,
`customizations.xml` e `Workflows/*.json`. Ainda não cobre tabelas Dataverse —
esta solução não exporta entidades —, então as regras de coluna (NM020–NM023)
seguem sem validação contra artefato real.

Para gerar: no make.powerapps.com, **Soluções → sua solução → Exportar → Não
gerenciada**. Marque a inclusão de metadados de tabela se quiser exercitar as
regras de coluna. Salve o `.zip` aqui com esse nome.

Antes de commitar: remova nomes de pessoas, e-mails, URLs de ambiente e qualquer
dado de cliente.

## solucao-dataverse/

Solução descompactada real, com três tabelas do Dataverse (`gmx_Expense`,
`gmx_ExpenseCategory`, `gmx_ExpenseReport`) e 75 colunas entre customizadas e do sistema.

Origem: [pnp/powerplatform-samples](https://github.com/pnp/powerplatform-samples),
amostra `ai-driven-expense-report-processing`, licença MIT. Só o manifesto e os
`Entity.xml` foram versionados; formulários, consultas salvas e o restante da solução
ficaram de fora por não interessarem às regras.

É o formato que o `pac solution unpack` produz e que os times versionam no repositório —
`Other/Solution.xml` em vez de `solution.xml` na raiz. Serve às regras de nomenclatura de
coluna, que sem ele seriam escritas contra suposição.

O que este artefato ensinou, e que nenhum fixture sintético teria ensinado:

- `crfbf_ExpenseReport` usa prefixo de outro publisher que não o `gmx` da solução — o
  achado que a NM020 existe para pegar, num artefato publicado.
- `gmx_amount_Base` traz `IsCustomField=1` **apesar de ser gerada pelo Dataverse** para o
  par de moeda. Filtrar só por `IsCustomField` produziria falso positivo; o discriminador
  honesto é `<CalculationOf>`, que diz de qual coluna ela deriva.
- `statecode`, `statuscode` e as colunas de auditoria são minúsculas e do sistema. Uma
  regra de PascalCase que não filtrasse por coluna customizada acusaria dezenas delas.

## solucao-dataverse-exportada.zip

O mesmo conteúdo de `solucao-dataverse/`, na embalagem que a exportação do portal produz:
`solution.xml` na raiz e as tabelas dentro de `customizations.xml`, em vez de separadas em
`Entities/<nome>/Entity.xml`.

Foi remontado a partir dos mesmos `Entity.xml` — o `pac solution unpack` faz exatamente a
operação inversa, movendo o XML de dentro do `customizations.xml` para arquivos. Os
elementos `<Entity>` são os originais, byte a byte; só a embalagem muda.

Existe porque exportar do portal é o caminho que o usuário aciona, e os dois formatos
precisam produzir os mesmos achados — se divergirem, um dos dois caminhos está lendo
errado. É o que o teste `FindsTheSameIssuesAsTheUnpackedLayout` verifica.
