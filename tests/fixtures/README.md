# Fixtures de artefatos reais

Coloque aqui soluções exportadas reais, com dados anonimizados, para validar
os extractors contra o formato de verdade — e não apenas contra os JSONs
sintéticos usados nos testes unitários.

Convenção de nomes:

- `solucao-exemplo.zip` — solução exportada com ao menos um canvas app e um cloud flow.

Os testes que dependem destes arquivos são pulados automaticamente quando eles
não existem, para que a suíte continue verde em quem clonou o repositório sem
os fixtures.

Antes de commitar um fixture: remova nomes de pessoas, e-mails, URLs de
ambiente e qualquer dado de cliente.

## Como exportar

No Power Apps (make.powerapps.com): **Soluções → selecione a solução →
Exportar solução → Não gerenciada**. O `.zip` baixado é exatamente o que o
`pp-lint` consome.

Para incluir o schema das tabelas Dataverse (necessário para as regras NM020
e NM021, da Fase 2), marque a opção de incluir metadados de tabela na exportação.
