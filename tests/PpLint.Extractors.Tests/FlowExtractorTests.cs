namespace PpLint.Extractors.Tests;

public class FlowExtractorTests
{
    private const string FlowJson = """
    {
      "properties": {
        "definition": {
          "triggers": {
            "Quando_um_item_e_criado": { "type": "OpenApiConnection", "inputs": {} }
          },
          "actions": {
            "Inicializar_contador": {
              "type": "InitializeVariable",
              "inputs": { "variables": [ { "name": "varContador", "type": "integer", "value": 0 } ] },
              "runAfter": {}
            },
            "Apply_to_each": {
              "type": "Foreach",
              "foreach": "@body('Get_items')?['value']",
              "runAfter": { "Inicializar_contador": [ "Succeeded" ] },
              "actions": {
                "Enviar_email": {
                  "type": "OpenApiConnection",
                  "inputs": { "parameters": { "emailMessage/Subject": "Olá @{items('Apply_to_each')?['Title']}" } },
                  "runAfter": {}
                }
              }
            }
          }
        }
      }
    }
    """;

    [Fact]
    public void Extract_ReadsTrigger()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido");
        Assert.NotNull(flow);
        Assert.Equal("Quando_um_item_e_criado", flow!.Trigger!.Name);
        Assert.Equal("OpenApiConnection", flow.Trigger.Type);
    }

    [Fact]
    public void Extract_ReadsTopLevelActions()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        Assert.Equal(["Inicializar_contador", "Apply_to_each"], flow.Actions.Select(a => a.Name));
    }

    [Fact]
    public void Extract_ReadsNestedActions()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        Assert.Equal(
            ["Inicializar_contador", "Apply_to_each", "Enviar_email"],
            flow.AllActions().Select(a => a.Name));
    }

    [Fact]
    public void Extract_ReadsRunAfter()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        var loop = flow.AllActions().Single(a => a.Name == "Apply_to_each");
        Assert.Equal(["Inicializar_contador"], loop.RunAfter);
    }

    [Fact]
    public void Extract_ReadsInitializedVariables()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        var v = Assert.Single(flow.Variables);
        Assert.Equal("varContador", v.Name);
        Assert.Equal("integer", v.Type);
    }

    [Fact]
    public void Extract_CollectsStringsFromInputsAsExpressions()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        var email = flow.AllActions().Single(a => a.Name == "Enviar_email");
        Assert.Contains(email.Expressions, e => e.Contains("items('Apply_to_each')"));
    }

    [Fact]
    public void Extract_ForeachExpressionIsCollected()
    {
        var flow = FlowExtractor.Extract(FlowJson, "sol.zip", "Workflows/f.json", "AprovarPedido")!;
        var loop = flow.AllActions().Single(a => a.Name == "Apply_to_each");
        Assert.Contains(loop.Expressions, e => e.Contains("body('Get_items')"));
    }

    [Fact]
    public void Extract_AcceptsDefinitionAtRoot()
    {
        const string json = """
        { "definition": { "actions": { "Compose": { "type": "Compose", "inputs": "x" } } } }
        """;
        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Single(flow.Actions);
    }

    [Fact]
    public void Extract_ReadsElseBranchOfCondition()
    {
        const string json = """
        {
          "definition": {
            "actions": {
              "Condicao": {
                "type": "If",
                "actions": { "Sim": { "type": "Compose", "inputs": "a" } },
                "else": { "actions": { "Nao": { "type": "Compose", "inputs": "b" } } }
              }
            }
          }
        }
        """;
        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Equal(["Condicao", "Sim", "Nao"], flow.AllActions().Select(a => a.Name));
    }

    [Fact]
    public void Extract_ReturnsNullForUnrecognizedJson()
    {
        Assert.Null(FlowExtractor.Extract("{\"outra\":1}", "sol.zip", "Workflows/f.json", "F"));
    }

    [Fact]
    public void Extract_ReturnsNullForMalformedJson()
    {
        Assert.Null(FlowExtractor.Extract("{ nao e json", "sol.zip", "Workflows/f.json", "F"));
    }
}

public class FlowDescriptionTests
{
    [Fact]
    public void Extract_ReadsActionDescription()
    {
        const string json = """
        {
          "definition": {
            "actions": {
              "Compor": {
                "type": "Compose",
                "description": "pp-lint: disable=FL201",
                "inputs": "x"
              }
            }
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Equal("pp-lint: disable=FL201", Assert.Single(flow.Actions).Description);
    }

    [Fact]
    public void Extract_ActionWithoutDescriptionHasNull()
    {
        const string json = """
        { "definition": { "actions": { "Compor": { "type": "Compose", "inputs": "x" } } } }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Null(Assert.Single(flow.Actions).Description);
    }

    [Fact]
    public void Extract_DescriptionOfNestedActionIsRead()
    {
        const string json = """
        {
          "definition": {
            "actions": {
              "Loop": {
                "type": "Foreach",
                "actions": {
                  "Interno": { "type": "Compose", "description": "nota do autor", "inputs": "y" }
                }
              }
            }
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;
        Assert.Equal("nota do autor", flow.AllActions().Single(a => a.Name == "Interno").Description);
    }
}

public class FlowMetadataTests
{
    [Fact]
    public void Extract_ReadsRecurrenceFromTrigger()
    {
        const string json = """
        {
          "properties": {
            "definition": {
              "triggers": {
                "Recorrencia": {
                  "type": "Recurrence",
                  "recurrence": { "frequency": "Day", "interval": 1, "timeZone": "UTC" }
                }
              },
              "actions": {}
            }
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Equal("Day", flow.Trigger!.Recurrence!.Frequency);
        Assert.Equal(1, flow.Trigger.Recurrence.Interval);
    }

    [Fact]
    public void Extract_TriggerWithoutRecurrenceHasNull()
    {
        const string json = """
        { "definition": { "triggers": { "Quando": { "type": "OpenApiConnection" } }, "actions": {} } }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Null(flow.Trigger!.Recurrence);
    }

    [Fact]
    public void Extract_ReadsFlowDescription()
    {
        const string json = """
        {
          "properties": {
            "description": "Envia o resumo diário",
            "definition": { "actions": {} }
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Equal("Envia o resumo diário", flow.Description);
    }

    [Fact]
    public void Extract_FlowWithoutDescriptionHasNull()
    {
        const string json = """
        { "properties": { "definition": { "actions": {} } } }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Null(flow.Description);
    }

    [Fact]
    public void Extract_IntervalDefaultsToOneWhenAbsent()
    {
        const string json = """
        {
          "definition": {
            "triggers": { "R": { "type": "Recurrence", "recurrence": { "frequency": "Hour" } } },
            "actions": {}
          }
        }
        """;

        var flow = FlowExtractor.Extract(json, "sol.zip", "Workflows/f.json", "F")!;

        Assert.Equal(1, flow.Trigger!.Recurrence!.Interval);
    }
}
