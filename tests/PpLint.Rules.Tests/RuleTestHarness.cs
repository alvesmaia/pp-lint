using PpLint.Core;
using PpLint.Core.Model;
using PpLint.Core.Rules;

namespace PpLint.Rules.Tests;

/// <summary>Construtores enxutos de IR para testar regras sem tocar em arquivos.</summary>
public static class RuleTestHarness
{
    private static SourceLocation Loc(string? symbol) => new("teste.msapp", "Controls/1.json", symbol, 0, 0);

    public static Control Ctl(string name, string template, params (string Property, string Script)[] properties)
    {
        var control = new Control { Name = name, TemplateName = template, Location = Loc(name) };
        foreach (var (property, script) in properties)
            control.Properties.Add(new PowerFxProperty(property, script, Loc($"{name}.{property}")));
        return control;
    }

    public static Control Screen(string name, params Control[] children)
    {
        var screen = new Control { Name = name, TemplateName = "screen", IsScreen = true, Location = Loc(name) };
        foreach (var child in children)
            screen.AddChild(child);
        return screen;
    }

    public static CanvasApp App(string name, params Control[] screens)
    {
        var app = new CanvasApp { Name = name, Location = Loc(null) };
        app.Screens.AddRange(screens);
        return app;
    }

    public static PowerPlatformProject ProjectWith(params CanvasApp[] apps)
    {
        var project = new PowerPlatformProject { SourcePath = "teste.msapp" };
        project.Apps.AddRange(apps);
        return project;
    }

    public static PowerPlatformProject ProjectWithFlows(params CloudFlow[] flows)
    {
        var project = new PowerPlatformProject { SourcePath = "teste.zip" };
        project.Flows.AddRange(flows);
        return project;
    }

    public static LintResult Run(IRule rule, PowerPlatformProject project, PpLintConfig? config = null) =>
        new RuleEngine([rule]).Run(project, config ?? PpLintConfig.Default);
}
