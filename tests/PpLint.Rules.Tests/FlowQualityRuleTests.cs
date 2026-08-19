using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;

namespace PpLint.Rules.Tests;

public class FlowQualityRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static PowerPlatformProject ProjectWith(CloudFlow flow)
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);
        return project;
    }

    private static CloudFlow FlowWith(
        string? description,
        params (string Name, string[] RunAfter, string State)[] actions)
    {
        var flow = new CloudFlow { Name = "AprovarPedido", Description = description, Location = Loc("AprovarPedido") };

        foreach (var (name, runAfter, state) in actions)
        {
            var action = new FlowAction { Name = name, Type = "Compose", Location = Loc(name) };
            action.RunAfter.AddRange(runAfter);
            action.RunAfterStates.Add(state);
            flow.Actions.Add(action);
        }

        return flow;
    }

    private static LintResult Run(IRule rule, CloudFlow flow) =>
        new RuleEngine([rule]).Run(ProjectWith(flow), PpLintConfig.Default);

    [Fact]
    public void FL210_ReportsFlowWithoutFailureBranch()
    {
        var flow = FlowWith(null, ("A", [], "Succeeded"), ("B", ["A"], "Succeeded"));
        var d = Assert.Single(Run(new NoErrorHandlingRule(), flow).Diagnostics);

        Assert.Equal("FL210", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("AprovarPedido", d.Message);
    }

    [Fact]
    public void FL210_AcceptsFlowWithFailureBranch()
    {
        var flow = FlowWith(null, ("A", [], "Succeeded"), ("Avisa", ["A"], "Failed"));

        Assert.Empty(Run(new NoErrorHandlingRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL210_IgnoresSingleActionFlow()
    {
        // Sem encadeamento não há o que tratar.
        var flow = FlowWith(null, ("A", [], "Succeeded"));
        var result = Run(new NoErrorHandlingRule(), flow);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void FL210_EvaluatesOneTargetPerFlow()
    {
        var flow = FlowWith(null, ("A", [], "Succeeded"), ("B", ["A"], "Succeeded"));

        Assert.Equal(1, Assert.Single(Run(new NoErrorHandlingRule(), flow).Tallies).Evaluated);
    }

    [Fact]
    public void FL240_ReportsFlowWithoutDescription()
    {
        var flow = FlowWith(null, ("A", [], "Succeeded"));
        var d = Assert.Single(Run(new FlowWithoutDescriptionRule(), flow).Diagnostics);

        Assert.Equal("FL240", d.RuleId);
        Assert.Equal(Severity.Info, d.Severity);
    }

    [Fact]
    public void FL240_AcceptsFlowWithDescription()
    {
        var flow = FlowWith("Envia o resumo diário", ("A", [], "Succeeded"));

        Assert.Empty(Run(new FlowWithoutDescriptionRule(), flow).Diagnostics);
    }

    [Fact]
    public void FL240_TreatsBlankDescriptionAsMissing()
    {
        var flow = FlowWith("   ", ("A", [], "Succeeded"));

        Assert.Single(Run(new FlowWithoutDescriptionRule(), flow).Diagnostics);
    }
}
