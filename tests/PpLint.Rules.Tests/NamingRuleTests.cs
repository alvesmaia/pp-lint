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

public class GeneratedControlTests
{
    [Fact]
    public void NM010_IgnoresGalleryTemplateGeneratedByStudio()
    {
        // galleryTemplate é criado dentro de toda galeria e não é renomeável.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("galleryTemplate6", "galleryTemplate"))));
        var result = Run(new DefaultControlNameRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, Assert.Single(result.Tallies).Evaluated); // só a tela
    }

    [Fact]
    public void NM010_IgnoresDataCardGeneratedByForm()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("DataCard4", "dataCard"))));
        Assert.Empty(Run(new DefaultControlNameRule(), project).Diagnostics);
    }

    [Fact]
    public void NM011_IgnoresGeneratedControls()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("galleryTemplate6", "galleryTemplate"))));
        var result = Run(new ControlPrefixRule(), project);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void GeneratedTemplatesAreConfigurable()
    {
        // Quem quiser rigor total esvazia a lista e volta a receber os achados.
        var config = PpLintConfig.Default with
        {
            Naming = new NamingConfig { GeneratedControlTemplates = new HashSet<string>() },
        };
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("galleryTemplate6", "galleryTemplate"))));

        Assert.Single(Run(new DefaultControlNameRule(), project, config).Diagnostics);
    }

    [Fact]
    public void RealControlsAreStillReported()
    {
        // A exclusão não pode engolir controles que o usuário de fato criou.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("Image1", "image"), Ctl("Slider1", "slider"))));
        Assert.Equal(2, Run(new DefaultControlNameRule(), project).Diagnostics.Count);
    }
}

public class MisleadingPrefixTests
{
    [Fact]
    public void Reports_PrefixThatBelongsToAnotherControlType()
    {
        // Copiar-colar clássico: nasceu botão, virou toggle, ninguém renomeou.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("btnTeste", "toggleSwitch"))));
        var d = Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics);

        Assert.Equal("NM011", d.RuleId);
        Assert.Contains("btn", d.Message);          // o prefixo enganoso
        Assert.Contains("button", d.Message);       // o tipo que ele sugere
        Assert.Contains("toggleSwitch", d.Message); // o tipo real
        Assert.Contains("tgl", d.Message);          // o prefixo correto
    }

    [Fact]
    public void GenericMessage_WhenNameHasNoKnownPrefix()
    {
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("SalvarPedido", "button"))));
        var d = Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics);

        Assert.DoesNotContain("sugere", d.Message);
        Assert.Contains("btn", d.Message);
    }

    [Fact]
    public void GenericMessage_WhenPrefixIsUnknown()
    {
        // 'svg' não é prefixo de nenhum tipo configurado.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("svgTabuleiro", "button"))));
        var d = Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics);

        Assert.DoesNotContain("sugere", d.Message);
    }

    [Fact]
    public void PrefixMustBeFollowedByUppercaseOrDigit()
    {
        // 'imgs' não é o prefixo 'img' seguido de camelCase — é outra palavra.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("imgsDoProduto", "button"))));
        var d = Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics);

        Assert.DoesNotContain("sugere", d.Message);
    }

    [Fact]
    public void StillReportsOneViolationPerControl()
    {
        var project = ProjectWith(App("A", Screen("scrHome",
            Ctl("btnTeste", "toggleSwitch"),
            Ctl("lblOutro", "toggleSwitch"))));

        var result = Run(new ControlPrefixRule(), project);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Equal(2, Assert.Single(result.Tallies).Evaluated);
    }

    [Fact]
    public void SharedPrefixPicksAStableType()
    {
        // 'lbl' serve a label e textcanvas: a mensagem precisa ser determinística.
        var project = ProjectWith(App("A", Screen("scrHome", Ctl("lblTitulo", "toggleSwitch"))));

        var first = Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics).Message;
        var second = Assert.Single(Run(new ControlPrefixRule(), project).Diagnostics).Message;

        Assert.Equal(first, second);
        Assert.Contains("lbl", first);
    }
}
