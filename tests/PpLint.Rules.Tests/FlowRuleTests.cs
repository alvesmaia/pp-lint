using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Rules.Flow;
using static PpLint.Rules.Tests.RuleTestHarness;

namespace PpLint.Rules.Tests;

public class UnusedFlowVariableRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static CloudFlow FlowWith(
        (string Name, string Type)[] variables,
        params (string ActionName, string[] Expressions)[] actions)
    {
        var flow = new CloudFlow { Name = "AprovarPedido", Location = Loc("AprovarPedido") };

        foreach (var (name, type) in variables)
            flow.Variables.Add(new FlowVariable(name, type, Loc("Inicializar")));

        foreach (var (actionName, expressions) in actions)
        {
            var action = new FlowAction { Name = actionName, Type = "Compose", Location = Loc(actionName) };
            action.Expressions.AddRange(expressions);
            flow.Actions.Add(action);
        }

        return flow;
    }

    [Fact]
    public void Reports_VariableThatIsNeverRead()
    {
        var flow = FlowWith([("varContador", "integer")], ("Compor", ["texto qualquer"]));
        var d = Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);

        Assert.Equal("FL201", d.RuleId);
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains("varContador", d.Message);
    }

    [Fact]
    public void DoesNotReport_VariableReadInAnExpression()
    {
        var flow = FlowWith([("varContador", "integer")], ("Compor", ["@{variables('varContador')}"]));
        Assert.Empty(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ReadWithSpacesInsideParentheses()
    {
        var flow = FlowWith([("varContador", "integer")], ("Compor", ["@{variables( 'varContador' )}"]));
        Assert.Empty(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void ReadDetectionIsCaseInsensitive()
    {
        var flow = FlowWith([("varContador", "integer")], ("Compor", ["@{VARIABLES('VARCONTADOR')}"]));
        Assert.Empty(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void Reports_VariableOnlyWritten()
    {
        // O nome aparece nos inputs do SetVariable, mas escrita não é leitura.
        var flow = FlowWith([("varContador", "integer")], ("Definir", ["varContador", "10"]));
        Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_VariableReadInsideNestedAction()
    {
        var flow = FlowWith([("varContador", "integer")]);
        var loop = new FlowAction { Name = "Apply_to_each", Type = "Foreach", Location = Loc("Apply_to_each") };
        var inner = new FlowAction { Name = "Compor", Type = "Compose", Location = Loc("Compor") };
        inner.Expressions.Add("@{variables('varContador')}");
        loop.Children.Add(inner);
        flow.Actions.Add(loop);

        Assert.Empty(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void Evaluates_OneTargetPerVariable()
    {
        var flow = FlowWith(
            [("varA", "integer"), ("varB", "string")],
            ("Compor", ["@{variables('varA')}"]));

        var tally = Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Tallies);
        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void Evaluates_ZeroWhenFlowHasNoVariables()
    {
        var flow = FlowWith([], ("Compor", ["x"]));
        Assert.Equal(0, Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Tallies).Evaluated);
    }

    [Fact]
    public void DoesNotMatchVariableWithSimilarName()
    {
        var flow = FlowWith([("varConta", "integer")], ("Compor", ["@{variables('varContador')}"]));
        Assert.Single(Run(new UnusedFlowVariableRule(), ProjectWithFlows(flow)).Diagnostics);
    }

    [Fact]
    public void HandlesMultipleFlowsIndependently()
    {
        var usada = FlowWith([("varA", "integer")], ("Compor", ["@{variables('varA')}"]));
        var naoUsada = FlowWith([("varB", "integer")], ("Compor", ["nada"]));

        var result = Run(new UnusedFlowVariableRule(), ProjectWithFlows(usada, naoUsada));
        Assert.Single(result.Diagnostics);
        Assert.Equal(2, Assert.Single(result.Tallies).Evaluated);
    }
}
