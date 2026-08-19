using PpLint.Core.Rules;
using PpLint.Rules.Naming;

namespace PpLint.Extractors.Tests;

/// <summary>
/// Valida os extractors contra uma solução exportada de verdade.
/// Pulado automaticamente quando o fixture não está presente.
/// </summary>
public class RealArtifactTests
{
    private static string? FixturePath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "solucao-exemplo.zip");
        var full = Path.GetFullPath(candidate);
        return File.Exists(full) ? full : null;
    }

    [SkippableFact]
    public void Load_RealSolutionExtractsAppsOrFlows()
    {
        var path = FixturePath();
        Skip.If(path is null, "Fixture tests/fixtures/solucao-exemplo.zip não encontrado.");

        var project = ProjectLoader.Load(path!);

        Assert.NotNull(project.Solution);
        Assert.True(
            project.Apps.Count > 0 || project.Flows.Count > 0,
            "Nenhum app nem fluxo foi extraído da solução real — o formato diverge do esperado.");
    }

    [SkippableFact]
    public void Load_RealSolutionAppHasControlsAndFormulas()
    {
        var path = FixturePath();
        Skip.If(path is null, "Fixture não encontrado.");

        var project = ProjectLoader.Load(path!);
        Skip.If(project.Apps.Count == 0, "A solução de exemplo não contém canvas apps.");

        var app = project.Apps[0];
        Assert.NotEmpty(app.Screens);
        Assert.True(
            app.AllControls().Any(c => c.Properties.Count > 0),
            "Nenhum controle trouxe fórmulas Power Fx — verifique o caminho Controls/*.json e o campo InvariantScript.");
    }

    [SkippableFact]
    public void Rules_RunOnRealSolutionWithoutCrashing()
    {
        var path = FixturePath();
        Skip.If(path is null, "Fixture não encontrado.");

        var project = ProjectLoader.Load(path!);
        var result = RuleEngine.CreateDefault(typeof(DefaultControlNameRule).Assembly)
            .Run(project, PpLint.Core.PpLintConfig.Default);

        Assert.Equal(5, result.Tallies.Count);
        Assert.True(
            result.Tallies.Any(t => t.Evaluated > 0),
            "Nenhuma regra examinou alvo algum numa solução real.");
    }
}
