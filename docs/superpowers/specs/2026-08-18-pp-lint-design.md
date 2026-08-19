# pp-lint — Design

Data: 2026-08-18
Status: aprovado para planejamento

## 1. Objetivo

Linter estático para artefatos do Power Platform, inspirado no ruff: um binário
único, rápido, sem dependências de runtime, que analisa uma solução exportada e
emite um relatório de conformidade.

Analisa:

- Código Power Fx de canvas apps (expressões de controles, `App.OnStart`, componentes).
- Fluxos do Power Automate Cloud (definição Logic Apps).
- Nomenclatura de colunas de dados (tabelas Dataverse, listas SharePoint e demais
  data sources referenciadas).
- Nomenclatura de controles, componentes, telas, variáveis, coleções e fluxos.

Detecta as mesmas classes de problema que o ruff detecta em Python: símbolos não
utilizados, desvio de convenção de nomes, lógica redundante ou constante, código
morto, duplicação e antipadrões de performance.

## 2. Decisões fundamentais

| Decisão | Escolha | Motivo |
|---|---|---|
| Stack | .NET 8 / C# | Único ecossistema com parser oficial de Power Fx (`Microsoft.PowerFx.Core`); permite análise semântica sobre AST em vez de regex |
| Distribuição | Binário público em GitHub Releases | Usuário baixa e roda, sem instalar .NET; multiplataforma |
| Arquitetura | IR unificado + motor de regras | Regras independem do formato de entrada; viabiliza regras cross-artefato |
| Nomenclatura | Preset embutido + override em TOML | Útil sem configuração; adaptável à convenção de cada empresa |
| Metadados de dados | Somente offline, do artefato | Zero credencial, zero rede, roda em qualquer máquina e em CI |
| Escrita | Nenhuma — read-only | Sem `--fix`; o produto é o relatório. Elimina o risco de corromper artefato |
| Índice de conformidade | Taxa de conformidade ponderada por severidade | Auditável, sem constante arbitrária, comparável entre apps de tamanhos distintos |

## 3. Entradas

Todos os formatos são ZIP e são lidos nativamente com `System.IO.Compression`.
Não há dependência do `pac CLI` nem de qualquer ferramenta externa.

### 3.1 `.msapp` (canvas app)

Lido cru, sem unpack:

| Caminho | Conteúdo | Uso |
|---|---|---|
| `Controls/*.json` | árvore `TopParent → Children`; cada controle tem `Name`, `Template.Name`, `Rules[]` com `Property` + `InvariantScript` | controles, nomenclatura, todo o código Power Fx |
| `DataSources/DataSources.json` | schema materializado das colunas usadas (SharePoint, Dataverse, SQL) | nomenclatura de colunas, data source órfã |
| `Connections/Connections.json` | conectores | conector não usado, conector de risco |
| `Properties.json`, `Header.json` | `OnStart`, formato do app, versão | regras de app-level |
| `ComponentReferences.json` | componentes de biblioteca | nomenclatura de componentes |
| `AppCheckerResult.sarif` | resultado do App Checker da Microsoft | ingerido como fonte complementar, opt-in, sem duplicar regras próprias |

### 3.2 Solução `.zip`

- `solution.xml` — publisher, prefixo, versão, managed/unmanaged.
- `customizations.xml` — mapa de workflows e entidades.
- `Workflows/*.json` — definição dos cloud flows (schema Logic Apps).
- `Entities/*/Entity.xml` — colunas Dataverse (`LogicalName`, `SchemaName`, `Type`, obrigatoriedade).
- `CanvasApps/*.msapp` — recursão para o extractor de app.

### 3.3 Pasta descompactada / repositório Git

Mesmos extractors apontados para um diretório, incluindo `Src/*.fx.yaml` de apps
já versionados via `pac canvas unpack`. É o que viabiliza uso sobre o repositório,
não só sobre o export.

## 4. Modelo de domínio (IR)

```
PowerPlatformProject
├─ Solution        Publisher, Prefix, Version, Managed
├─ Apps[]          CanvasApp
│   ├─ Screens[]   → Controls[] (árvore)
│   │                 Control: Name, TemplateType, Parent,
│   │                          Properties[] → PowerFxExpression
│   ├─ Components[] (canvas components + custom properties)
│   ├─ DataSources[] Name, Kind, Columns[]
│   └─ Variables[]  (inferidas: global | context | collection)
├─ Flows[]         CloudFlow
│   ├─ Trigger      Kind, Recurrence, Inputs
│   ├─ Actions[]    Name, Type, RunAfter[], Inputs, Expressions[]
│   └─ Variables[]  (InitializeVariable / SetVariable / AppendTo…)
├─ Tables[]        LogicalName, SchemaName, Columns[] (Name, Type, Required)
└─ SourceMap       toda entidade guarda ArtifactPath + ponteiro de posição
```

`PowerFxExpression` guarda o texto original e o `ParseResult` do
`Microsoft.PowerFx.Core` (`TexlNode` raiz). Regras semânticas percorrem a AST com
`TexlVisitor`; análise textual nunca é usada onde a AST resolve.

O `SourceMap` torna o diagnóstico acionável:
`MinhaSolucao.zip → CanvasApps/App.msapp → Controls/3.json → btnSalvar.OnSelect`,
com offset de caractere dentro da expressão (`Span` do Power Fx).

### 4.1 Grafos derivados

Construídos uma vez após a extração, consumidos por várias regras:

1. **Grafo de variáveis** — definições (`Set`, `UpdateContext`, `Navigate` com
   contexto, `Collect`/`ClearCollect`; `InitializeVariable`/`SetVariable` nos
   fluxos) contra leituras (identificadores na AST / expressões do fluxo).
2. **Grafo de referências** — controle→controle, controle→data source,
   app→tabela/coluna, fluxo→tabela/coluna, tela→tela via `Navigate`.
3. **Índice de similaridade** — hash estrutural de ASTs com identificadores
   anonimizados, e de sequências de ações de fluxos, para detectar duplicação.

## 5. Estrutura da solução

```
src/PpLint.Core        IR, SourceMap, Diagnostic, RuleEngine, config, scoring
src/PpLint.Extractors  MsappExtractor, SolutionExtractor, FxYamlExtractor, FlowExtractor
src/PpLint.PowerFx     wrapper do Microsoft.PowerFx.Core, visitors, similaridade
src/PpLint.Rules       uma classe por regra, agrupadas por categoria
src/PpLint.Cli         System.CommandLine, renderers (text/json/sarif/html/md)
tests/                 unit por regra + fixtures de artefatos reais anonimizados
```

Dependência unidirecional: `Rules` conhece `Core`, nunca `Extractors`.

## 6. Contrato de regra

```csharp
[Rule("PF101", Category.PowerFx, Severity.Warning)]
public sealed class UnusedGlobalVariable : IRule
{
    public void Check(LintContext ctx)
    {
        foreach (var v in ctx.Project.GlobalVariables)
        {
            ctx.Evaluated(1);                 // denominador do índice
            if (!ctx.Graph.IsRead(v))
                ctx.Report(v.Location, "Variável global '{0}' é definida e nunca lida", v.Name);
        }
    }
}
```

Requisitos:

- `Id` estável, nunca reciclado.
- Severidade default sobrescrevível na configuração.
- Toda regra chama `ctx.Evaluated(n)` para cada alvo examinado — sem isso o índice
  de conformidade é impossível de calcular corretamente.
- Cada regra tem documentação em markdown embutido como recurso, servindo tanto ao
  `pp-lint explain` quanto à geração do site de documentação.

## 7. Catálogo de regras

Prefixos: `NM` nomenclatura, `PF` Power Fx, `FL` fluxos, `DUP` duplicação e código
morto, `PERF` performance e delegação, `SEC` segurança, `SOL` solução/ALM.

### 7.1 NM — Nomenclatura

| Id | Regra | Sev |
|---|---|---|
| NM001 | Variável global fora do padrão (`varPascalCase`) | Warn |
| NM002 | Variável de contexto fora do padrão (`locPascalCase`) | Warn |
| NM003 | Coleção fora do padrão (`colPascalCase`) | Warn |
| NM010 | Controle com nome default não renomeado (`Button1`, `Label12`) | Error |
| NM011 | Prefixo do controle não corresponde ao template | Warn |
| NM012 | Tela fora do padrão (`scrPascalCase`) | Warn |
| NM013 | Componente canvas fora do padrão (`cmpPascalCase`) | Warn |
| NM014 | Propriedade customizada de componente fora do padrão | Info |
| NM020 | Coluna Dataverse sem o prefixo do publisher da solução | Error |
| NM021 | `SchemaName` de coluna Dataverse fora de PascalCase | Warn |
| NM022 | Coluna SharePoint com espaço ou acento (vira `_x0020_` nas fórmulas) | Error |
| NM023 | Display name divergente do schema name além do aceitável | Info |
| NM030 | Fluxo com nome default ou fora do padrão verbo-substantivo | Warn |
| NM031 | Ação de fluxo com nome default (`Compose 2`, `Apply_to_each_3`) | Error |
| NM040 | Identificador com acento, espaço ou caractere especial | Error |
| NM041 | Nome abaixo do comprimento mínimo | Info |

Todas as regras `NM` leem regex e tabela de prefixos da configuração; nenhuma
convenção é hardcoded em C#.

### 7.2 PF — Power Fx

| Id | Regra | Sev |
|---|---|---|
| PF101 | Variável global definida e nunca lida | Warn |
| PF102 | Variável de contexto definida e nunca lida | Warn |
| PF103 | Coleção criada e nunca usada | Warn |
| PF104 | Variável lida mas nunca definida | Error |
| PF105 | Variável global usada em uma única tela — deveria ser de contexto | Info |
| PF106 | `Set` redundante: mesmo valor atribuído duas vezes sem leitura entre eles | Warn |
| PF110 | Condição constante (`2 > 1`, `true And true`) | Error |
| PF111 | `If(cond, true, false)` redutível a `cond` | Warn |
| PF112 | Comparação com booleano (`If(x = true, …)`) | Warn |
| PF113 | Ramos idênticos (`If(c, X, X)`) | Error |
| PF114 | Ramo inalcançável (`If(c, a, If(c, b, d))`) | Error |
| PF115 | Dupla negação (`Not(Not(x))`) | Warn |
| PF116 | `Filter(ds, true)` — filtro sem efeito | Warn |
| PF117 | `CountRows(Filter(…)) > 0` em vez de `!IsEmpty(Filter(…))` | Warn |
| PF118 | Concatenação com literal vazio (`& ""`) | Info |
| PF120 | `UpdateContext` em `App.OnStart` (não funciona) | Error |
| PF121 | Referência a controle de outra tela | Warn |
| PF122 | `ForAll` com `Collect` em vez de operação em massa | Warn |
| PF123 | Aninhamento de expressão acima do limite configurado | Warn |
| PF124 | Cor ou dimensão hardcoded em vez de variável de tema | Info |
| PF125 | Texto literal exibido ao usuário fora de tabela de tradução | Info |
| PF130 | Expressão não compila (erro do parser Power Fx) | Error |

### 7.3 FL — Power Automate

| Id | Regra | Sev |
|---|---|---|
| FL201 | Variável inicializada e nunca usada | Warn |
| FL202 | Variável usada antes de inicializada | Error |
| FL203 | Ação cujo output nunca é consumido | Warn |
| FL210 | Fluxo sem nenhum tratamento de erro | Error |
| FL211 | Escopo de catch que não relata a falha | Warn |
| FL212 | Ação desabilitada deixada no fluxo | Info |
| FL220 | Consulta sem `$filter`, com filtragem feita depois em condição | Warn |
| FL221 | `Get items` sem `Top Count` nem paginação | Warn |
| FL222 | `Apply to each` aninhado | Warn |
| FL223 | `Apply to each` com concorrência desligada sobre volume alto | Info |
| FL224 | Chamada de conector dentro de loop que caberia em lote | Warn |
| FL230 | Recorrência mais frequente que o limite configurado | Warn |
| FL231 | Trigger sem condição de gatilho, saindo por condição logo em seguida | Warn |
| FL240 | Fluxo sem descrição | Info |
| FL241 | Expressão inválida ou referência a ação inexistente | Error |

### 7.4 DUP, PERF, SEC, SOL

| Id | Regra | Sev |
|---|---|---|
| DUP301 | Expressão Power Fx idêntica repetida N+ vezes | Warn |
| DUP302 | Sequência de ações idêntica entre fluxos | Info |
| DUP303 | Controle nunca referenciado e invisível — código morto | Warn |
| DUP304 | Tela inalcançável (nenhum `Navigate` aponta para ela) | Warn |
| DUP305 | Data source declarada e nunca usada | Warn |
| DUP306 | Conexão declarada e nunca usada | Warn |
| PERF401 | Função não delegável sobre data source delegável | Error |
| PERF402 | `App.OnStart` com chamadas sequenciais que caberiam em `Concurrent` | Warn |
| PERF403 | `App.OnStart` acima do orçamento de operações | Warn |
| SEC501 | Segredo, chave ou token literal em expressão ou input de fluxo | Error |
| SEC502 | URL de ambiente hardcoded em vez de variável de ambiente | Warn |
| SEC503 | Conector de risco sem justificativa | Warn |
| SOL601 | Componente na solução sem o prefixo do publisher | Warn |
| SOL602 | Referência a coluna ou tabela inexistente no schema extraído | Error |
| SOL603 | Solução managed sendo analisada como fonte | Info |

## 8. Índice de conformidade

Cada regra reporta violações (`Report`) e alvos examinados (`Evaluated`). O índice
é a razão entre checagens aprovadas e checagens realizadas, ponderada por
severidade.

Pesos: `Error = 10`, `Warning = 3`, `Info = 1`.

Para um conjunto de regras S (uma categoria, um artefato ou o projeto inteiro):

```
Conformidade(S) = 100 × ( 1 − Σ(r∈S) w(r)·V(r) / Σ(r∈S) w(r)·E(r) )
```

onde `V(r)` são as violações e `E(r)` os alvos avaliados pela regra r. Regras
desabilitadas não entram em nenhum dos somatórios.

Invariante obrigatório: toda regra emite **no máximo uma violação por alvo
avaliado**, garantindo `V(r) ≤ E(r)` e portanto resultado sempre em `[0, 100]`.
Regras agregadas escolhem o alvo de forma coerente com esse invariante — por
exemplo, DUP301 avalia *grupos de expressões equivalentes*, não expressões
individuais, e reporta uma violação por grupo duplicado. O teste de contrato do
catálogo (seção 12) verifica esse invariante em todas as regras.

Exemplo: NM011 examinou 358 controles e reprovou 46 → contribui `3·46` no
numerador e `3·358` no denominador.

O relatório apresenta:

- Índice global do projeto.
- Índice por categoria (Nomenclatura, Power Fx, Fluxos, Duplicação, Performance,
  Segurança, Solução), com contagem absoluta ao lado do percentual.
- Índice por artefato, ordenado — ranking de qual app ou fluxo está pior.
- Delta em relação à baseline, quando `--baseline` é usado.

Não há nota em letra nem estimativa de esforço: o número é sempre "X% dos itens
verificados estão conformes", verificável a partir das contagens exibidas.

## 9. Configuração

`pp-lint.toml`, procurado no diretório atual e nos ancestrais. Todos os valores têm
default utilizável; rodar sem configuração funciona.

```toml
[pp-lint]
select = ["ALL"]
ignore = ["PF125", "FL240"]
severity-overrides = { NM011 = "error", PERF401 = "warning" }
fail-on = "error"

[pp-lint.naming]
global-variable   = "^var[A-Z][A-Za-z0-9]*$"
context-variable  = "^loc[A-Z][A-Za-z0-9]*$"
collection        = "^col[A-Z][A-Za-z0-9]*$"
screen            = "^scr[A-Z][A-Za-z0-9]*$"
component         = "^cmp[A-Z][A-Za-z0-9]*$"
dataverse-column  = "^[a-z]{2,8}_[A-Za-z0-9]+$"

[pp-lint.naming.control-prefixes]
Button = "btn"
Label = "lbl"
TextInput = "txt"
Gallery = "gal"
Icon = "ico"
Image = "img"
GroupContainer = "cnt"
Form = "frm"

[pp-lint.thresholds]
max-expression-nesting = 4
min-duplicate-occurrences = 3
max-onstart-operations = 10
min-recurrence-minutes = 15

[pp-lint.per-artifact-ignores]
"**/Legado*.msapp" = ["NM010", "NM011"]
```

Precedência: defaults embutidos → `pp-lint.toml` → flags de CLI.

## 10. CLI

```
pp-lint check <caminho...> [opções]     analisa .zip, .msapp, pasta ou glob
pp-lint explain <ID>                    documentação da regra
pp-lint rules [--format json]           lista o catálogo completo
pp-lint inspect <artefato>              dump do IR extraído (debug/suporte)
pp-lint baseline <caminho...>           grava as violações atuais como linha de base
```

Opções: `--format text|json|sarif|html|md`, `--output`, `--select`, `--ignore`,
`--fail-on`, `--baseline`, `--config`, `--no-color`, `--quiet`, `--verbose`.

Exit codes: `0` sem achados acima de `fail-on`; `1` achados acima de `fail-on`;
`2` erro de execução.

O linter abre todo artefato em modo somente-leitura e nunca escreve no artefato.

## 11. Relatório

Saída padrão no terminal, agrupada por artefato e ordenada por severidade:

```
MinhaSolucao.zip
  CanvasApps/AppVendas.msapp
    scrPedidos › btnSalvar.OnSelect:12:8
      error   PF104  Variável 'varClienteSelecionado' é lida mas nunca definida
      warning PF111  If(IsBlank(x), true, false) pode ser escrito como IsBlank(x)
    Screen1
      error   NM010  Controle com nome default; renomeie seguindo o padrão 'scr'
  Workflows/AprovarPedido.json
    Apply_to_each_3 › Get_items
      warning FL220  Filtragem feita após a consulta; use $filter no OData

  Conformidade geral: 91,4%
    Nomenclatura   87,2%  (312/358 conformes)
    Power Fx       94,6%  (1044/1103)
    Fluxos         89,1%  (147/165)
    Duplicação     97,0%
    Performance    99,1%
    Segurança     100,0%

  Resumo: 4 erros, 11 avisos, 6 informações em 3 artefatos (1,2 s)
  Top ofensores: NM011 (7×), DUP301 (5×), PF111 (3×)
```

Formatos adicionais: **JSON** (integração), **SARIF** (anotações inline em PR),
**HTML** autocontido (relatório navegável para CoE, com os mesmos índices) e
**Markdown** (colar em issue ou PR). O bloco de conformidade e resumo sempre
aparece, inclusive com `--quiet`.

### 11.1 Supressão e adoção incremental

- Power Fx: `// pp-lint: disable=PF111` na linha anterior ou na mesma linha.
- Fluxos: JSON não aceita comentário — usa-se o campo `description` da ação com
  `pp-lint: disable=FL212`.
- Configuração: `per-artifact-ignores` por glob.
- `pp-lint baseline` grava as violações atuais em `pp-lint-baseline.json`; execuções
  seguintes só falham em violação nova. É o que permite adotar o linter num app
  legado sem parar o time.

Regras suprimidas saem do cálculo do índice (numerador e denominador), para que
suprimir não infle artificialmente a conformidade.

## 12. Testes

- **Golden files por regra**: fixture mínimo por regra com snapshot do diagnóstico
  esperado, incluindo fixture negativo provando que a regra não dispara em código
  correto. Falso positivo destrói a confiança mais rápido que regra faltando.
- **Testes de extractor** sobre artefatos reais anonimizados em `tests/fixtures/`.
- **Teste de contrato do catálogo**: todo `Id` registrado tem documentação, teste
  positivo, teste negativo e chamada a `Evaluated`. O build falha caso contrário.
- **Testes do índice**: casos com contagens conhecidas verificando a fórmula,
  incluindo o efeito de regra suprimida e de projeto sem nenhuma violação.
- **Robustez**: artefato corrompido, zip vazio, `.msapp` de versão antiga, expressão
  que não parseia — reporta e continua, sem exceção não tratada.
- **Benchmark**: solução com 50+ telas deve concluir em poucos segundos.

## 13. Distribuição

- GitHub Actions com matriz `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`,
  publicando via `dotnet publish -p:PublishAot=true`, com fallback para single-file
  self-contained caso alguma dependência não seja compatível com AOT.
- Release automática por tag `v*`, com checksums SHA256 e changelog gerado.
- Também publicado como `dotnet tool` no NuGet.
- Repositório público, licença MIT, documentação das regras gerada a partir dos
  mesmos markdowns usados pelo `explain`.

## 14. Faseamento

| Fase | Entrega |
|---|---|
| 1 | Fundação: IR, extractors `.zip`/`.msapp`, CLI, renderer de terminal, cálculo do índice, 5 regras piloto (NM010, NM011, PF101, PF110, FL201) |
| 2 | Catálogo NM e PF completo, grafo de variáveis, configuração TOML, formatos JSON e SARIF |
| 3 | Catálogo FL completo, grafo de referências, SOL602 |
| 4 | DUP (similaridade estrutural), PERF (tabela de delegação por conector), SEC |
| 5 | Relatório HTML, baseline, release pública, documentação |

## 15. Fora de escopo

- Qualquer escrita em artefato, incluindo `--fix` e renomeação automática.
- Conexão a APIs do Dataverse, Graph ou Power Platform; a análise é sempre offline.
- Model-driven apps, PCF, Power Pages e Power BI.
- Servidor LSP e extensão de editor.
- Regras declarativas escritas pelo usuário; reavaliar após a 1.0 conforme demanda.
