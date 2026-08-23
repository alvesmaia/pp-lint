# PERF402 — Chamadas de rede em sequência no `OnStart`

## O que pega

Três ou mais chamadas que tocam fontes de dados externas, no `OnStart`, fora de um `Concurrent`.

Chamadas já dentro de `Concurrent` não contam — elas já fazem o que a regra recomenda. Chamadas sobre coleções e tabelas literais também não: `ClearCollect(colA, [1,2,3])` não espera por nada.

## Por que importa

O `OnStart` roda antes de a primeira tela aparecer. Cada chamada de rede sequencial soma sua latência à anterior, e o usuário fica olhando para o logotipo enquanto isso.

`Concurrent` dispara todas juntas: o tempo de abertura passa a ser o da chamada mais lenta, em vez da soma de todas. Com quatro chamadas de 400 ms, a diferença é 1,6 s contra 0,4 s.

Três é o limiar porque, com duas, a diferença raramente é perceptível e o `Concurrent` só atrapalha a leitura.

## Exemplo

Ruim:

```
ClearCollect(colPedidos, Pedidos);
ClearCollect(colClientes, Clientes);
ClearCollect(colProdutos, Produtos)
```

Bom:

```
Concurrent(
    ClearCollect(colPedidos, Pedidos),
    ClearCollect(colClientes, Clientes),
    ClearCollect(colProdutos, Produtos)
)
```

Cuidado: dentro de `Concurrent` a ordem não é garantida. Se uma chamada depende do resultado de outra, elas precisam continuar em sequência.
