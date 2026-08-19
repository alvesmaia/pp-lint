using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;

namespace PpLint.Rules.Tests;

public class FlowStructureRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static LintResult Run(IRule rule, CloudFlow flow, PpLintConfig? config = null)
    {
        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);
        return new RuleEngine([rule]).Run(project, config ?? PpLintConfig.Default);
    }

    private static FlowAction Loop(string name, params FlowAction[] children)
    {
        var loop = new FlowAction { Name = name, Type = "Foreach", Location = Loc(name) };
        loop.Children.AddRange(children);
        return loop;
    }

    private static CloudFlow FlowWith(params FlowAction[] actions)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.AddRange(actions);
        return flow;
    }

    private static CloudFlow FlowWithRecurrence(string frequency, int interval)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Trigger = new FlowTrigger("R", "Recurrence", Loc("R"), new FlowRecurrence(frequency, interval));
        return flow;
    }

    [Fact]
    public void FL222_ReportsNestedLoop()
    {
        var interno = Loop("Interno");
        var d = Assert.Single(Run(new NestedLoopRule(), FlowWith(Loop("Externo", interno))).Diagnostics);

        Assert.Equal("FL222", d.RuleId);
        Assert.Contains("Interno", d.Message);
    }

    [Fact]
    public void FL222_AcceptsSiblingLoops()
    {
        // Dois laços lado a lado não se multiplicam.
        Assert.Empty(Run(new NestedLoopRule(), FlowWith(Loop("A"), Loop("B"))).Diagnostics);
    }

    [Fact]
    public void FL222_AcceptsLoopWithOrdinaryChildren()
    {
        var interna = new FlowAction { Name = "Compor", Type = "Compose", Location = Loc("Compor") };

        Assert.Empty(Run(new NestedLoopRule(), FlowWith(Loop("Externo", interna))).Diagnostics);
    }

    [Fact]
    public void FL222_EvaluatesEveryLoop()
    {
        var result = Run(new NestedLoopRule(), FlowWith(Loop("A"), Loop("B", Loop("C"))));
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(3, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void FL230_ReportsEveryFiveMinutes()
    {
        var d = Assert.Single(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Minute", 5)).Diagnostics);

        Assert.Equal("FL230", d.RuleId);
        Assert.Contains("5", d.Message);
    }

    [Fact]
    public void FL230_ReportsEverySecond()
    {
        Assert.Single(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Second", 30)).Diagnostics);
    }

    [Fact]
    public void FL230_AcceptsDailyRecurrence() =>
        Assert.Empty(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Day", 1)).Diagnostics);

    [Fact]
    public void FL230_AcceptsHourlyRecurrence() =>
        Assert.Empty(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Hour", 1)).Diagnostics);

    [Fact]
    public void FL230_IgnoresFlowWithoutRecurrence()
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Trigger = new FlowTrigger("Quando", "OpenApiConnection", Loc("Quando"));

        var result = Run(new FrequentRecurrenceRule(), flow);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void FL230_RespectsConfiguredThreshold()
    {
        var config = PpLintConfig.Default with
        {
            Thresholds = new ThresholdConfig { MinRecurrenceMinutes = 1 },
        };

        Assert.Empty(Run(new FrequentRecurrenceRule(), FlowWithRecurrence("Minute", 5), config).Diagnostics);
    }
}
