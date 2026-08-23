# PERF403 — `OnStart` acima do orçamento

## O que pega

Mais de oito chamadas a fontes de dados externas no `OnStart`. Aqui as chamadas dentro de `Concurrent` contam: paralelizar reduz o tempo, não o volume de dados que desce antes da primeira tela.

## Por que importa

Tudo que está no `OnStart` acontece antes de o usuário ver qualquer coisa. Um app que carrega oito tabelas na abertura demora a abrir mesmo com tudo paralelo, porque os dados ainda precisam trafegar.

A pergunta que a regra provoca é: quantas dessas tabelas o app inteiro precisa, e quantas só uma tela usa? O que só uma tela usa pode ser carregado quando aquela tela abrir — o usuário paga o custo apenas se for até lá.

## Exemplo

Ruim: `OnStart` com `ClearCollect` de nove tabelas, três das quais só a tela de relatórios consulta.

Bom: as seis do app inteiro no `OnStart`, dentro de `Concurrent`; as três de relatório no `OnVisible` da tela de relatórios.

## Sobre o limiar

Oito é um ponto de partida, não uma verdade. Um app interno numa rede rápida aguenta mais; um app de campo em rede móvel aguenta menos. O que a regra oferece é o momento de fazer a pergunta.
