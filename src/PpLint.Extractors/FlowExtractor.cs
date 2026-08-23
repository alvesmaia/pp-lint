using System.Text.Json;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Extractors;

/// <summary>
/// Extrai o modelo de um cloud flow a partir da definição Logic Apps.
/// Em 'triggers' e 'actions', a chave do objeto é o nome da ação.
/// </summary>
public static class FlowExtractor
{
    private static readonly string[] ContainerProperties = ["actions"];

    public static CloudFlow? Extract(string json, string artifactPath, string entryPath, string flowName)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var definition = FindDefinition(doc.RootElement);
            if (definition is null)
                return null;

            var flow = new CloudFlow
            {
                Name = flowName,
                Description = FlowDescription(doc.RootElement),
                Location = new SourceLocation(artifactPath, entryPath, flowName, 0, 0),
            };

            if (definition.Value.TryGetProperty("triggers", out var triggers)
                && triggers.ValueKind == JsonValueKind.Object)
            {
                foreach (var t in triggers.EnumerateObject())
                {
                    flow.Trigger = new FlowTrigger(
                        t.Name,
                        GetString(t.Value, "type") ?? string.Empty,
                        new SourceLocation(artifactPath, entryPath, t.Name, 0, 0),
                        ReadRecurrence(t.Value));

                    flow.TriggerHasCondition =
                        t.Value.TryGetProperty("conditions", out var cond)
                        && cond.ValueKind == JsonValueKind.Array
                        && cond.GetArrayLength() > 0;
                    break; // um fluxo tem exatamente um trigger
                }
            }

            if (definition.Value.TryGetProperty("actions", out var actions)
                && actions.ValueKind == JsonValueKind.Object)
            {
                foreach (var action in ReadActions(actions, artifactPath, entryPath, flow))
                    flow.Actions.Add(action);
            }

            return flow;
        }
    }

    private static JsonElement? FindDefinition(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object
            && props.TryGetProperty("definition", out var nested)
            && nested.ValueKind == JsonValueKind.Object)
            return nested;

        if (root.TryGetProperty("definition", out var direct) && direct.ValueKind == JsonValueKind.Object)
            return direct;

        if (root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Object)
            return root;

        return null;
    }

    private static List<FlowAction> ReadActions(
        JsonElement actions, string artifactPath, string entryPath, CloudFlow flow)
    {
        var result = new List<FlowAction>();

        foreach (var property in actions.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;

            var action = new FlowAction
            {
                Name = property.Name,
                Type = GetString(property.Value, "type") ?? string.Empty,
                Description = GetString(property.Value, "description"),
                ConcurrencyDegree = ReadConcurrency(property.Value),
                Location = new SourceLocation(artifactPath, entryPath, property.Name, 0, 0),
            };

            if (property.Value.TryGetProperty("runAfter", out var runAfter)
                && runAfter.ValueKind == JsonValueKind.Object)
            {
                foreach (var predecessor in runAfter.EnumerateObject())
                {
                    action.RunAfter.Add(predecessor.Name);

                    // O valor é a lista de estados aceitos do predecessor. É aí que
                    // aparece o tratamento de erro: qualquer estado diferente de
                    // Succeeded significa "faça isto se aquilo não deu certo".
                    if (predecessor.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var estado in predecessor.Value.EnumerateArray())
                        {
                            var texto = estado.GetString();
                            if (!string.IsNullOrEmpty(texto))
                                action.RunAfterStates.Add(texto);
                        }
                    }
                }
            }

            foreach (var name in new[] { "inputs", "foreach", "expression", "condition" })
            {
                if (property.Value.TryGetProperty(name, out var value))
                    CollectStrings(value, action.Expressions);
            }

            CollectVariables(property.Value, action, artifactPath, entryPath, flow);

            foreach (var containerProperty in ContainerProperties)
            {
                if (property.Value.TryGetProperty(containerProperty, out var child)
                    && child.ValueKind == JsonValueKind.Object)
                {
                    action.Children.AddRange(ReadActions(child, artifactPath, entryPath, flow));
                }
            }

            if (property.Value.TryGetProperty("else", out var elseBranch)
                && elseBranch.ValueKind == JsonValueKind.Object
                && elseBranch.TryGetProperty("actions", out var elseActions)
                && elseActions.ValueKind == JsonValueKind.Object)
            {
                action.Children.AddRange(ReadActions(elseActions, artifactPath, entryPath, flow));
            }

            result.Add(action);
        }

        return result;
    }

    private static void CollectVariables(
        JsonElement actionElement, FlowAction action, string artifactPath, string entryPath, CloudFlow flow)
    {
        if (!action.Type.Equals("InitializeVariable", StringComparison.OrdinalIgnoreCase))
            return;

        if (!actionElement.TryGetProperty("inputs", out var inputs)
            || inputs.ValueKind != JsonValueKind.Object
            || !inputs.TryGetProperty("variables", out var variables)
            || variables.ValueKind != JsonValueKind.Array)
            return;

        foreach (var v in variables.EnumerateArray())
        {
            var name = GetString(v, "name");
            if (name is null)
                continue;

            flow.Variables.Add(new FlowVariable(
                name,
                GetString(v, "type") ?? string.Empty,
                new SourceLocation(artifactPath, entryPath, action.Name, 0, 0)));
        }
    }

    /// <summary>Coleta recursivamente toda string do elemento — é onde vivem as expressões @{...}.</summary>
    private static void CollectStrings(JsonElement element, List<string> target)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrEmpty(s))
                    target.Add(s);
                break;
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                    CollectStrings(p.Value, target);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectStrings(item, target);
                break;
        }
    }

    /// <summary>
    /// O grau de concorrência de um laço, em
    /// runtimeConfiguration.concurrency.repetitions. Ausente significa
    /// sequencial, que é o padrão do Power Automate.
    /// </summary>
    private static int? ReadConcurrency(JsonElement action)
    {
        if (!action.TryGetProperty("runtimeConfiguration", out var runtime)
            || !runtime.TryGetProperty("concurrency", out var concurrency)
            || !concurrency.TryGetProperty("repetitions", out var reps)
            || reps.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return reps.GetInt32();
    }

    /// <summary>
    /// A descrição fica em properties.description, fora da definition — e é o
    /// único lugar onde ela aparece no arquivo exportado.
    /// </summary>
    private static string? FlowDescription(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("properties", out var props)
        && props.ValueKind == JsonValueKind.Object
            ? GetString(props, "description")
            : null;

    private static FlowRecurrence? ReadRecurrence(JsonElement trigger)
    {
        if (!trigger.TryGetProperty("recurrence", out var recurrence)
            || recurrence.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var frequency = GetString(recurrence, "frequency");
        if (frequency is null)
            return null;

        // Frequência sem intervalo declarado significa de um em um.
        var interval = recurrence.TryGetProperty("interval", out var i)
                       && i.ValueKind == JsonValueKind.Number
            ? i.GetInt32()
            : 1;

        return new FlowRecurrence(frequency, interval);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
