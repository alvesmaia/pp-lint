using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;

namespace PpLint.Rules.Tests;

public class ExecutionOrderRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static FlowAction Action(string name, string type, string[] runAfter, params string[] expressions)
    {
        var action = new FlowAction { Name = name, Type = type, Location = Loc(name) };
        action.RunAfter.AddRange(runAfter);
        action.RunAfterStates.Add("Succeeded");
        action.Expressions.AddRange(expressions);
        return action;
    }

    private static LintResult Run(IRule rule, CloudFlow flow)
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);
        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void FL202_ReportsReadBeforeInitialization()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("Usa", "Compose", [], "@{variables('varTotal')}"));
        flow.Actions.Add(Action("Inicializa", "InitializeVariable", ["Usa"]));
        flow.Variables.Add(new FlowVariable("varTotal", "integer", Loc("Inicializa")));

        var d = Assert.Single(Run(new VariableUsedBeforeInitializationRule(), flow).Diagnostics);

        Assert.Equal("FL202", d.RuleId);
        Assert.Contains("varTotal", d.Message);
    }

    [Fact]
    public void FL202_AcceptsCorrectOrder()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("Inicializa", "InitializeVariable", []));
        flow.Actions.Add(Action("Usa", "Compose", ["Inicializa"], "@{variables('varTotal')}"));
        flow.Variables.Add(new FlowVariable("varTotal", "integer", Loc("Inicializa")));

        Assert.Empty(Run(new VariableUsedBeforeInitializationRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL202_StaysQuietWhenOrderIsUnknown()
    {
        // Ramos independentes: o formato não diz qual roda primeiro.
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("Inicializa", "InitializeVariable", []));
        flow.Actions.Add(Action("Usa", "Compose", [], "@{variables('varTotal')}"));
        flow.Variables.Add(new FlowVariable("varTotal", "integer", Loc("Inicializa")));

        Assert.Empty(Run(new VariableUsedBeforeInitializationRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL202_EvaluatesOneTargetPerVariable()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("Inicializa", "InitializeVariable", []));
        flow.Actions.Add(Action("Usa", "Compose", ["Inicializa"], "@{variables('varTotal')}"));
        flow.Variables.Add(new FlowVariable("varTotal", "integer", Loc("Inicializa")));

        Assert.Equal(1, Assert.Single(Run(new VariableUsedBeforeInitializationRule(), flow).Tallies).Evaluated);
    }

    [Fact]
    public void FL241_ReportsRunAfterPointingToNothing()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("A", "Compose", ["AcaoQueNaoExiste"]));

        var d = Assert.Single(Run(new UnknownPredecessorRule(), flow).Diagnostics);

        Assert.Equal("FL241", d.RuleId);
        Assert.Contains("AcaoQueNaoExiste", d.Message);
    }

    [Fact]
    public void FL241_AcceptsValidReferences()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("A", "Compose", []));
        flow.Actions.Add(Action("B", "Compose", ["A"]));

        Assert.Empty(Run(new UnknownPredecessorRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL241_EvaluatesOneTargetPerFlow()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.Add(Action("A", "Compose", []));

        Assert.Equal(1, Assert.Single(Run(new UnknownPredecessorRule(), flow).Tallies).Evaluated);
    }
}
