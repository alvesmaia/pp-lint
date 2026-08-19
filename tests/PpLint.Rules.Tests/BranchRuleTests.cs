using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Fx;

namespace PpLint.Rules.Tests;

public class BranchRuleTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void PF113_ReportsIdenticalBranches()
    {
        var d = Assert.Single(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA + 1, varA + 1))").Diagnostics);

        Assert.Equal("PF113", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void PF113_IgnoresFormattingDifferences()
    {
        Assert.Single(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA+1, varA + 1))").Diagnostics);
    }

    [Fact]
    public void PF113_AcceptsDifferentBranches() =>
        Assert.Empty(Run(new IdenticalBranchesRule(), "Set(varX, If(varCond, varA, varB))").Diagnostics);

    [Fact]
    public void PF113_AcceptsTwoArgumentIf() =>
        Assert.Empty(Run(new IdenticalBranchesRule(), "If(varCond, Notify(\"oi\"))").Diagnostics);

    [Fact]
    public void PF113_EvaluatesEveryThreeArgumentIf()
    {
        var result = Run(new IdenticalBranchesRule(), "Set(varA, If(varX, 1, 1)); Set(varB, If(varY, 1, 2))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void PF114_ReportsRepeatedConditionInElse()
    {
        var d = Assert.Single(Run(new UnreachableBranchRule(),
            "Set(varX, If(varCond, 1, If(varCond, 2, 3)))").Diagnostics);

        Assert.Equal("PF114", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("varCond", d.Message);
    }

    [Fact]
    public void PF114_AcceptsDifferentConditions() =>
        Assert.Empty(Run(new UnreachableBranchRule(),
            "Set(varX, If(varA, 1, If(varB, 2, 3)))").Diagnostics);

    [Fact]
    public void PF114_AcceptsRepeatedConditionInThenBranch()
    {
        // If(c, If(c, ...), ...) é redundante mas alcançável; não é esta regra.
        Assert.Empty(Run(new UnreachableBranchRule(),
            "Set(varX, If(varCond, If(varCond, 1, 2), 3))").Diagnostics);
    }

    [Fact]
    public void PF114_EvaluatesEveryNestedElse()
    {
        var result = Run(new UnreachableBranchRule(),
            "Set(varA, If(varX, 1, If(varX, 2, 3))); Set(varB, If(varY, 1, If(varZ, 2, 3)))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}

public class LogicRuleCorrectnessTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void PF113_IgnoresVolatileFunctions()
    {
        // Rand() devolve valor diferente a cada chamada: os ramos não são iguais.
        Assert.Empty(Run(new IdenticalBranchesRule(), "Set(varX, If(varC, Rand(), Rand()))").Diagnostics);
    }

    [Fact]
    public void PF113_IgnoresNowInBothBranches() =>
        Assert.Empty(Run(new IdenticalBranchesRule(), "Set(varX, If(varC, Now(), Now()))").Diagnostics);

    [Fact]
    public void PF114_IgnoresVolatileCondition()
    {
        // Rand() > 0.5 testado duas vezes pode dar resultados diferentes; o ramo
        // interno é alcançável.
        Assert.Empty(Run(new UnreachableBranchRule(),
            "Set(varX, If(Rand() > 0.5, 1, If(Rand() > 0.5, 2, 3)))").Diagnostics);
    }

    [Fact]
    public void PF113_StillReportsPureIdenticalBranches() =>
        Assert.Single(Run(new IdenticalBranchesRule(), "Set(varX, If(varC, varA + 1, varA + 1))").Diagnostics);

    [Fact]
    public void PF112_EvaluatesEveryComparisonNotOnlyTheReported()
    {
        // Contar só o que reporta faria Evaluated == Violations, e a conformidade
        // da regra seria sempre 0% quando ela achasse algo.
        var result = Run(new BooleanComparisonRule(),
            "If(varA = true, Notify(\"x\")); If(varB = 1, Notify(\"y\"))");
        var tally = Assert.Single(result.Tallies);

        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }

    [Fact]
    public void PF115_ReportsTripleNegationOnce()
    {
        // Uma cadeia de negações é um problema só, no nó mais externo.
        Assert.Single(Run(new DoubleNegationRule(), "Set(varX, Not(Not(Not(varAtivo))))").Diagnostics);
    }

    [Fact]
    public void PF117_OnlyCountsComparisonsAsTargets()
    {
        // CountRows usado em aritmética não é alvo desta regra e não pode entrar
        // no denominador.
        var result = Run(new CountRowsComparisonRule(), "Set(varX, CountRows(colA) + 1)");

        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void PF118_StaysQuietOnNumberToTextIdiom()
    {
        // 'varQtd & ""' é a forma corrente de converter número em texto;
        // mandar remover mudaria o tipo do resultado.
        Assert.Empty(Run(new EmptyConcatenationRule(), "Set(varTexto, varQtd & \"\")").Diagnostics);
    }

    [Fact]
    public void PF118_StillReportsConcatenationOfTwoTexts() =>
        Assert.Single(Run(new EmptyConcatenationRule(), "Set(varX, \"abc\" & \"\")").Diagnostics);
}

/// <summary>
/// If(c1, r1, c2, r2, senão) é a forma encadeada do mesmo desvio que
/// If(c1, r1, If(c2, r2, senão)) escreve aninhado. As regras precisam enxergar
/// as duas — um guard de 'exatamente 3 argumentos' deixa a metade do idioma real
/// passar sem análise.
/// </summary>
public class IfChainTests
{
    private static SourceLocation Loc(string? s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, string script)
    {
        var screen = new Control { Name = "scrA", TemplateName = "screen", IsScreen = true, Location = Loc("scrA") };
        var button = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        button.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(button);

        var app = new CanvasApp { Name = "App", Location = Loc(null) };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void PF114_ReportsRepeatedConditionInChain()
    {
        var d = Assert.Single(Run(new UnreachableBranchRule(),
            "Set(varX, If(varA, 1, varB, 2, varA, 3, 4))").Diagnostics);

        Assert.Equal("PF114", d.RuleId);
        Assert.Contains("varA", d.Message);
    }

    [Fact]
    public void PF114_AcceptsChainOfDistinctConditions() =>
        Assert.Empty(Run(new UnreachableBranchRule(),
            "Set(varX, If(varA, 1, varB, 2, varC, 3, 4))").Diagnostics);

    [Fact]
    public void PF114_CountsNestedChainOnceNotOncePerLevel()
    {
        // If(a,1,If(b,2,If(c,3,4))) é uma cadeia só. Contar cada nível daria três
        // alvos para um desvio, distorcendo a conformidade da regra.
        var result = Run(new UnreachableBranchRule(), "Set(varX, If(varA, 1, If(varB, 2, If(varC, 3, 4))))");

        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void PF114_ReportsRepetitionAcrossNestingLevels() =>
        Assert.Single(Run(new UnreachableBranchRule(),
            "Set(varX, If(varA, 1, If(varB, 2, If(varA, 3, 4))))").Diagnostics);

    [Fact]
    public void PF113_ReportsChainWhereEveryBranchIsTheSame()
    {
        var d = Assert.Single(Run(new IdenticalBranchesRule(),
            "Set(varX, If(varA, varR, varB, varR, varR))").Diagnostics);

        Assert.Equal("PF113", d.RuleId);
    }

    [Fact]
    public void PF113_AcceptsChainWithOneDifferentBranch() =>
        Assert.Empty(Run(new IdenticalBranchesRule(),
            "Set(varX, If(varA, varR, varB, varR, varOutro))").Diagnostics);

    [Fact]
    public void PF113_IgnoresChainWithoutElse()
    {
        // Sem o 'senão', o resultado quando nenhuma condição vale é Blank(), que
        // é diferente dos ramos — a condição ainda decide algo.
        Assert.Empty(Run(new IdenticalBranchesRule(), "Set(varX, If(varA, varR, varB, varR))").Diagnostics);
    }
}
