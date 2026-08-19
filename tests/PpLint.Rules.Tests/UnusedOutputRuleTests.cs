using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Flow;

namespace PpLint.Rules.Tests;

public class UnusedOutputRuleTests
{
    private static SourceLocation Loc(string s) => new("sol.zip", "Workflows/f.json", s, 0, 0);

    private static FlowAction Action(string name, string type, params string[] expressions)
    {
        var action = new FlowAction { Name = name, Type = type, Location = Loc(name) };
        action.Expressions.AddRange(expressions);
        return action;
    }

    private static LintResult Run(params FlowAction[] actions)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        flow.Actions.AddRange(actions);

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        return new RuleEngine([new UnusedOutputRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsComposeNeverConsumed()
    {
        // A segunda ação tem efeito colateral, então não é alvo — o único achado
        // esperado é o Compose solto.
        var d = Assert.Single(Run(
            Action("Compor", "Compose", "valor"),
            Action("Enviar", "OpenApiConnection", "nada a ver")).Diagnostics);

        Assert.Equal("FL203", d.RuleId);
        Assert.Contains("Compor", d.Message);
    }

    [Fact]
    public void AcceptsOutputConsumedByOutputs() =>
        Assert.Empty(Run(
            Action("Compor", "Compose", "valor"),
            Action("Usa", "OpenApiConnection", "@{outputs('Compor')}")).Diagnostics);

    [Fact]
    public void AcceptsOutputConsumedByBody() =>
        Assert.Empty(Run(
            Action("Analisa", "ParseJson", "{}"),
            Action("Usa", "OpenApiConnection", "@{body('Analisa')}")).Diagnostics);

    [Fact]
    public void IgnoresConnectorActions()
    {
        // Enviar e-mail existe pelo efeito, não pela saída.
        var result = Run(
            Action("Enviar_email", "OpenApiConnection", "assunto"),
            Action("Outra", "Compose", "x"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("Enviar_email"));
    }

    [Fact]
    public void IgnoresVariableActions()
    {
        var result = Run(
            Action("Inicializa", "InitializeVariable", "x"),
            Action("Define", "SetVariable", "y"));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EvaluatesOneTargetPerComputationalAction()
    {
        var result = Run(
            Action("A", "Compose", "x"),
            Action("B", "Compose", "@{outputs('A')}"));

        var tally = Assert.Single(result.Tallies);
        Assert.Equal(2, tally.Evaluated);
        Assert.Equal(1, tally.Violations);
    }
}
