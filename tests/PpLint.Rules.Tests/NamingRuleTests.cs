using PpLint.Core;
using PpLint.Rules.Naming;
using static PpLint.Rules.Tests.RuleTestHarness;

namespace PpLint.Rules.Tests;

public class DefaultControlNameRuleTests
{
    [Fact]
    public void Reports_ControlNamedAfterItsTemplateWithNumber()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Button1", "button"))));
        var result = Run(new DefaultControlNameRule(), project);

        var d = Assert.Single(result.Diagnostics);
        Assert.Equal("NM010", d.RuleId);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("Button1", d.Message);
    }

    [Fact]
    public void Reports_ScreenWithDefaultName()
    {
        var project = ProjectWith(App("A", Screen("Screen1")));
        Assert.Single(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void Reports_MultiDigitDefaultName()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Label12", "label"))));
        Assert.Single(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_ProperlyNamedControl()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnSalvar", "button"))));
        Assert.Empty(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_NameThatMerelyContainsTemplateName()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnButton1Salvar", "button"))));
        Assert.Empty(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void DoesNotReport_NameEqualToTemplateWithoutNumber()
    {
        // "Button" sem dígito não é o nome que o Studio gera; não é violação desta regra.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Button", "button"))));
        Assert.Empty(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void Evaluates_EveryControlIncludingScreens()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnA", "button"), Ctl("btnB", "button"))));
        var tally = Assert.Single(Run(new DefaultControlNameRule(), project).Tallies);
        Assert.Equal(3, tally.Evaluated);
        Assert.Equal(0, tally.Violations);
    }

    [Fact]
    public void Evaluates_ZeroTargetsWhenThereAreNoApps()
    {
        var tally = Assert.Single(Run(new DefaultControlNameRule(), ProjectWith()).Tallies);
        Assert.Equal(0, tally.Evaluated);
    }
}

public class ControlPrefixRuleTests
{
    [Fact]
    public void Reports_ButtonWithoutBtnPrefix()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("SalvarPedido", "button"))));
        var d = Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics);

        Assert.Equal("NM011", d.RuleId);
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains("btn", d.Message);
        Assert.Contains("SalvarPedido", d.Message);
    }

    [Fact]
    public void DoesNotReport_ControlWithCorrectPrefix()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnSalvar", "button"), Ctl("lblTitulo", "label"))));
        Assert.Empty(Run(new ControlPrefixRule(), project).Diagnostics);
    }

    [Fact]
    public void DoesNotEvaluate_Screens()
    {
        var project = ProjectWith(App("A", Screen("QualquerNome", Ctl("btnOk", "button"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void DoesNotEvaluate_TemplateWithoutConfiguredPrefix()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("qualquerCoisa", "templateDesconhecido"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void DoesNotEvaluate_ControlWithDefaultName()
    {
        // Já reportado por NM010; contar aqui puniria o mesmo problema duas vezes no índice.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Button1", "button"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void UsesConfiguredPrefixInsteadOfPreset()
    {
        var config = PpLintConfig.Default with
        {
            Naming = new NamingConfig
            {
                ControlPrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["button"] = "bt",
                },
            },
        };
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btSalvar", "button"))));

        Assert.Empty(Run(new ControlPrefixRule(), project, config).Diagnostics);
    }

    [Fact]
    public void PrefixMatchIsCaseSensitiveOnTheName()
    {
        // "BtnSalvar" não atende ao prefixo "btn": a convenção é camelCase.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("BtnSalvar", "button"))));
        Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics);
    }

    [Fact]
    public void TemplateLookupIsCaseInsensitive()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnOk", "Button"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated);
    }
}
