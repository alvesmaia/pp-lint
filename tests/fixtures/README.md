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
