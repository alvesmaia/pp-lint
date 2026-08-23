# PERF401 — Função não delegável sobre fonte externa

## O que pega

Função que o Power Apps nunca delega — `Search`, `First`, `Last`, `FirstN`, `GroupBy`, `AddColumns`, `Distinct`, entre outras — aplicada sobre uma fonte de dados externa.

A regra enxerga a fonte através de funções aninhadas: `First(Sort(Pedidos, Data))` consulta `Pedidos` do mesmo jeito.

Coleções ficam de fora. Elas vivem na memória do dispositivo e nunca foram delegáveis; cobrá-las seria pedir o impossível.

## Por que importa

Quando a expressão não é delegável, o Power Apps baixa só os primeiros registros — 500 por padrão, 2000 no máximo — e avalia o resto no dispositivo.

O app **não dá erro**. Ele mostra a resposta calculada sobre um pedaço dos dados. Enquanto a tabela é pequena o resultado está certo por acaso, e o defeito só aparece meses depois, quando alguém repara que o total não bate ou que um registro sumiu da busca.

Severidade Erro por causa disso: o sintoma é dado errado apresentado como certo.

## Exemplo

Ruim, com `Pedidos` numa lista do SharePoint:

```
Search(Pedidos, txtBusca.Text, "Titulo")
```

Bom:

```
Filter(Pedidos, StartsWith(Titulo, txtBusca.Text))
```

`StartsWith` dentro de `Filter` é delegável no SharePoint. A delegabilidade exata depende do conector e do tipo da coluna — a documentação da Microsoft mantém a tabela por conector.

## Por que a lista de funções é curta

Delegabilidade varia por conector e por tipo de coluna. Esta regra lista apenas o que nenhum conector delega, e cala sobre o resto. Num achado de severidade Erro, falar menos e acertar é melhor que falar mais e errar.
