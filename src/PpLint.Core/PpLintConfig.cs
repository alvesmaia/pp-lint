namespace PpLint.Core;

/// <summary>
/// Convenções de nomenclatura. Os valores default são o preset embutido,
/// baseado no padrão de codificação de canvas apps da Microsoft.
/// A partir da Fase 2 estes valores passam a ser carregáveis de pp-lint.toml.
/// </summary>
public sealed record NamingConfig
{
    public string GlobalVariable { get; init; } = "^var[A-Z][A-Za-z0-9]*$";
    public string ContextVariable { get; init; } = "^loc[A-Z][A-Za-z0-9]*$";
    public string Collection { get; init; } = "^col[A-Z][A-Za-z0-9]*$";
    public string Screen { get; init; } = "^scr[A-Z][A-Za-z0-9]*$";
    public string Component { get; init; } = "^cmp[A-Z][A-Za-z0-9]*$";

    /// <summary>
    /// Templates de controles que o Power Apps Studio cria sozinho e que o
    /// desenvolvedor não nomeia — galleryTemplate nasce dentro de toda galeria
    /// e sequer é renomeável; dataCard é gerado a cada campo de formulário.
    /// Cobrá-los por convenção de nome seria reclamar de código que ninguém escreveu,
    /// então ficam fora das regras de nomenclatura, inclusive do denominador do índice.
    /// Esvazie esta lista para voltar a avaliá-los.
    /// </summary>
    public IReadOnlySet<string> GeneratedControlTemplates { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "galleryTemplate",
            "dataCard",
        };

    /// <summary>Nome do template do controle (minúsculo) para o prefixo esperado.</summary>
    public IReadOnlyDictionary<string, string> ControlPrefixes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["button"] = "btn",
            ["label"] = "lbl",
            ["text"] = "txt",
            ["textcanvas"] = "lbl",
            ["gallery"] = "gal",
            ["icon"] = "ico",
            ["image"] = "img",
            ["groupcontainer"] = "cnt",
            ["form"] = "frm",
            ["dropdown"] = "drp",
            ["combobox"] = "cmb",
            ["datepicker"] = "dtp",
            ["checkbox"] = "chk",
            ["toggleswitch"] = "tgl",
            ["radio"] = "rad",
            ["slider"] = "sld",
            ["htmlviewer"] = "htm",
            ["rectangle"] = "rec",
            ["timer"] = "tim",
        };
}

public sealed record PpLintConfig
{
    public IReadOnlySet<string> Ignore { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Severity> SeverityOverrides { get; init; } =
        new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase);

    public NamingConfig Naming { get; init; } = new();

    public static PpLintConfig Default { get; } = new();
}
