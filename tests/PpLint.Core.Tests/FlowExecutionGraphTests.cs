using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Core.Tests;

public class FlowExecutionGraphTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static CloudFlow FlowWith(params (string Name, string[] RunAfter, string State)[] actions)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };

        foreach (var (name, runAfter, state) in actions)
        {
            var action = new FlowAction { Name = name, Type = "Compose", Location = Loc(name) };
            action.RunAfter.AddRange(runAfter);
            if (state != "Succeeded")
                action.RunAfterStates.Add(state);
            flow.Actions.Add(action);
        }

        return flow;
    }

    [Fact]
    public void DirectDependencyOrdersActions()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("B", ["A"], "Succeeded")));

        Assert.True(graph.RunsBefore("A", "B"));
        Assert.False(graph.RunsBefore("B", "A"));
    }

    [Fact]
    public void OrderIsTransitive()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("B", ["A"], "Succeeded"),
            ("C", ["B"], "Succeeded")));

        Assert.True(graph.RunsBefore("A", "C"));
    }

    [Fact]
    public void IndependentActionsHaveNoOrder()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("B", [], "Succeeded")));

        Assert.False(graph.RunsBefore("A", "B"));
        Assert.False(graph.RunsBefore("B", "A"));
    }

    [Fact]
    public void CycleDoesNotHang()
    {
        // O formato não deveria permitir, mas um arquivo corrompido pode trazer.
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", ["B"], "Succeeded"),
            ("B", ["A"], "Succeeded")));

        Assert.True(graph.RunsBefore("A", "B"));
    }

    [Fact]
    public void UnknownPredecessorIsReported()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", ["NaoExiste"], "Succeeded")));

        Assert.Equal(["NaoExiste"], graph.UnknownPredecessors);
    }

    [Fact]
    public void FlowWithOnlySucceededDoesNotHandleFailure()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("B", ["A"], "Succeeded")));

        Assert.False(graph.HandlesFailure);
    }

    [Fact]
    public void FlowWithFailedBranchHandlesFailure()
    {
        var graph = FlowExecutionGraph.Build(FlowWith(
            ("A", [], "Succeeded"),
            ("Avisa", ["A"], "Failed")));

        Assert.True(graph.HandlesFailure);
    }

    [Fact]
    public void NestedActionsAreIncluded()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        var loop = new FlowAction { Name = "Loop", Type = "Foreach", Location = Loc("Loop") };
        var inner = new FlowAction { Name = "Interna", Type = "Compose", Location = Loc("Interna") };
        inner.RunAfterStates.Add("Failed");
        loop.Children.Add(inner);
        flow.Actions.Add(loop);

        Assert.True(FlowExecutionGraph.Build(flow).HandlesFailure);
    }
}
