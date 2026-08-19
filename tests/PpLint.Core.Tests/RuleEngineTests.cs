using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Core.Tests;

[Rule("XX001", RuleCategory.Naming, Severity.Warning)]
public sealed class AlwaysReportsRule : IRule
{
    public void Check(LintContext ctx)
    {
        ctx.Evaluated(3);
        ctx.Report(new SourceLocation("a.zip", "b.json", "alvo", 0, 0), "problema encontrado");
    }
}

[Rule("XX002", RuleCategory.PowerFx, Severity.Info)]
public sealed class NeverReportsRule : IRule
{
    public void Check(LintContext ctx) => ctx.Evaluated(5);
}

[Rule("XX003", RuleCategory.Flow, Severity.Error)]
public sealed class ThrowingRule : IRule
{
    public void Check(LintContext ctx) => throw new InvalidOperationException("regra com defeito");
}

[Rule("XX004", RuleCategory.PowerFx, Severity.Info)]
public sealed class ReportingInfoRule : IRule
{
    public void Check(LintContext ctx)
    {
        ctx.Evaluated(1);
        ctx.Report(new SourceLocation("a.zip", "b.json", "outro", 0, 0), "aviso informativo");
    }
}

public sealed class UnattributedRule : IRule
{
    public void Check(LintContext ctx) => ctx.Evaluated(1);
}

public class RuleEngineTests
{
    private static PowerPlatformProject EmptyProject() => new() { SourcePath = "a.zip" };

    [Fact]
    public void Run_ProducesDiagnosticWithRuleMetadata()
    {
        var engine = new RuleEngine([new AlwaysReportsRule()]);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        var d = Assert.Single(result.Diagnostics);
        Assert.Equal("XX001", d.RuleId);
        Assert.Equal(RuleCategory.Naming, d.Category);
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Equal("problema encontrado", d.Message);
    }

    [Fact]
    public void Run_RecordsTalliesForEveryRule()
    {
        var engine = new RuleEngine([new AlwaysReportsRule(), new NeverReportsRule()]);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        Assert.Equal(2, result.Tallies.Count);
        var t1 = result.Tallies.Single(t => t.RuleId == "XX001");
        Assert.Equal(3, t1.Evaluated);
        Assert.Equal(1, t1.Violations);

        var t2 = result.Tallies.Single(t => t.RuleId == "XX002");
        Assert.Equal(5, t2.Evaluated);
        Assert.Equal(0, t2.Violations);
    }

    [Fact]
    public void Run_IgnoredRuleDoesNotRun()
    {
        var config = PpLintConfig.Default with { Ignore = new HashSet<string>(["XX001"]) };
        var engine = new RuleEngine([new AlwaysReportsRule(), new NeverReportsRule()]);
        var result = engine.Run(EmptyProject(), config);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["XX002"], result.Tallies.Select(t => t.RuleId));
    }

    [Fact]
    public void Run_SeverityOverrideIsApplied()
    {
        var config = PpLintConfig.Default with
        {
            SeverityOverrides = new Dictionary<string, Severity> { ["XX001"] = Severity.Error },
        };
        var engine = new RuleEngine([new AlwaysReportsRule()]);
        var result = engine.Run(EmptyProject(), config);

        Assert.Equal(Severity.Error, Assert.Single(result.Diagnostics).Severity);
        Assert.Equal(Severity.Error, Assert.Single(result.Tallies).Severity);
    }

    [Fact]
    public void Run_RuleThatThrowsDoesNotAbortTheRun()
    {
        var engine = new RuleEngine([new ThrowingRule(), new AlwaysReportsRule()]);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        Assert.Single(result.Diagnostics, d => d.RuleId == "XX001");
        Assert.DoesNotContain(result.Tallies, t => t.RuleId == "XX003");
    }

    [Fact]
    public void Run_DiagnosticsAreSortedBySeverityDescending()
    {
        var engine = new RuleEngine([new AlwaysReportsRule(), new ReportingInfoRule()]);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Equal(Severity.Warning, result.Diagnostics[0].Severity);
        Assert.Equal(Severity.Info, result.Diagnostics[1].Severity);
    }

    [Fact]
    public void RuleWithoutAttribute_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new RuleEngine([new UnattributedRule()]));
        Assert.Contains("UnattributedRule", ex.Message);
    }

    [Fact]
    public void CreateDefault_DiscoversAttributedRulesInAssembly()
    {
        var engine = RuleEngine.CreateDefault(typeof(AlwaysReportsRule).Assembly);
        var result = engine.Run(EmptyProject(), PpLintConfig.Default);

        Assert.Contains(result.Tallies, t => t.RuleId == "XX001");
        Assert.Contains(result.Tallies, t => t.RuleId == "XX002");
    }

    [Fact]
    public void DefaultConfig_HasNamingPresets()
    {
        var naming = PpLintConfig.Default.Naming;
        Assert.Equal("^var[A-Z][A-Za-z0-9]*$", naming.GlobalVariable);
        Assert.Equal("btn", naming.ControlPrefixes["button"]);
        Assert.Equal("lbl", naming.ControlPrefixes["label"]);
    }
}
