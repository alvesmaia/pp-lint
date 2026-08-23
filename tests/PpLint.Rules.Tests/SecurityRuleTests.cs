using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;
using PpLint.Rules.Security;

namespace PpLint.Rules.Tests;

public class SecretRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(IRule rule, string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var c = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        c.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(c);

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    private static LintResult RunFlow(IRule rule, string expressao)
    {
        var flow = new CloudFlow { Name = "F", Location = Loc("F") };
        var acao = new FlowAction { Name = "Chamar", Type = "Http", Location = Loc("Chamar") };
        acao.Expressions.Add(expressao);
        flow.Actions.Add(acao);

        var project = new PowerPlatformProject { SourcePath = "sol.zip" };
        project.Flows.Add(flow);

        return new RuleEngine([rule]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsJwtToken()
    {
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghij";
        var d = Assert.Single(Run(new HardcodedSecretRule(), $"Set(varToken, \"{jwt}\")").Diagnostics);

        Assert.Equal("SEC501", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void ReportsSharedAccessSignature() =>
        Assert.Single(Run(new HardcodedSecretRule(),
            "Set(varUrl, \"https://x.blob.core.windows.net/c/f.txt?sv=2021-06-08&sig=abcdefghijklmnopqrstuvwxyz0123456789%3D\")").Diagnostics);

    [Fact]
    public void ReportsApiKeyAssignment() =>
        Assert.Single(Run(new HardcodedSecretRule(),
            "Set(varH, \"api_key=a1b2c3d4e5f6g7h8i9j0k1l2\")").Diagnostics);

    [Fact]
    public void ReportsSecretInFlowInput() =>
        Assert.Single(RunFlow(new HardcodedSecretRule(),
            "client_secret=Zx9KpQm2Vn7Rt4Ws1Yb6Hd3Fg8Jc5Le0").Diagnostics);

    [Fact]
    public void AcceptsOrdinaryText()
    {
        // Procurar a palavra 'senha' acusaria todo rótulo de tela de login. A
        // regra procura estrutura de credencial, não vocabulário.
        Assert.Empty(Run(new HardcodedSecretRule(),
            "Set(varMsg, \"Digite sua senha para continuar\")").Diagnostics);
    }

    [Fact]
    public void AcceptsShortIdentifiers() =>
        Assert.Empty(Run(new HardcodedSecretRule(), "Set(varId, \"abc123\")").Diagnostics);

    [Fact]
    public void EveryLiteralIsATarget()
    {
        var result = Run(new HardcodedSecretRule(), "Set(varA, \"um\"); Set(varB, \"dois\")");

        Assert.Equal(2, Assert.Single(result.Tallies).Evaluated);
        Assert.Equal(0, result.Tallies[0].Violations);
    }
}

public class EnvironmentUrlRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(string script)
    {
        var screen = new Control { Name = "scrHome", TemplateName = "screen", IsScreen = true, Location = Loc("scrHome") };
        var c = new Control { Name = "btnA", TemplateName = "button", Location = Loc("btnA") };
        c.Properties.Add(new PowerFxProperty("OnSelect", script, Loc("btnA.OnSelect")));
        screen.AddChild(c);

        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        app.Screens.Add(screen);

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new HardcodedEnvironmentUrlRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsDataverseEnvironmentUrl()
    {
        var d = Assert.Single(Run("Set(varUrl, \"https://contoso-dev.crm4.dynamics.com/api/data\")").Diagnostics);

        Assert.Equal("SEC502", d.RuleId);
        Assert.Contains("contoso-dev", d.Message);
    }

    [Fact]
    public void ReportsSharePointTenantUrl() =>
        Assert.Single(Run("Set(varSite, \"https://contoso.sharepoint.com/sites/Vendas\")").Diagnostics);

    [Fact]
    public void AcceptsPublicDocumentationLink()
    {
        // Link para documentação é uso legítimo e não muda entre ambientes.
        Assert.Empty(Run("Launch(\"https://learn.microsoft.com/power-apps\")").Diagnostics);
    }
}

public class RiskyConnectorRuleTests
{
    private static SourceLocation Loc(string s) => new("a.msapp", "Controls/1.json", s, 0, 0);

    private static LintResult Run(params (string Nome, string Tipo)[] fontes)
    {
        var app = new CanvasApp { Name = "App", Location = Loc("App") };
        foreach (var (nome, tipo) in fontes)
            app.DataSources.Add(new DataSource(nome, tipo, []));

        var project = new PowerPlatformProject { SourcePath = "a.msapp" };
        project.Apps.Add(app);

        return new RuleEngine([new RiskyConnectorRule()]).Run(project, PpLintConfig.Default);
    }

    [Fact]
    public void ReportsOutboundConnector()
    {
        var d = Assert.Single(Run(("Dropbox", "ServiceInfo")).Diagnostics);

        Assert.Equal("SEC503", d.RuleId);
        Assert.Contains("Dropbox", d.Message);
    }

    [Fact]
    public void AcceptsInternalConnector() =>
        Assert.Empty(Run(("Office365Users", "ServiceInfo"), ("SharePoint", "ServiceInfo")).Diagnostics);

    [Fact]
    public void EverySourceIsATarget()
    {
        var result = Run(("Office365Users", "ServiceInfo"), ("Dropbox", "ServiceInfo"));

        Assert.Equal(2, Assert.Single(result.Tallies).Evaluated);
        Assert.Equal(1, result.Tallies[0].Violations);
    }
}
