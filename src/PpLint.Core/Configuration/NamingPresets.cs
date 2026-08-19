namespace PpLint.Core.Configuration;

/// <summary>
/// Convenções de nomenclatura prontas. Impor uma régua única faz o linter
/// reclamar de apps bem escritos que apenas seguem outro padrão — o app real
/// usado para validar a Fase 1 gerou 129 avisos por usar PascalCase.
/// Aqui o time escolhe a régua; a lista de templates é a mesma em todos.
/// </summary>
public static class NamingPresets
{
    public const string DefaultName = "camel-prefix";

    private static readonly Dictionary<string, string> CamelPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = "btn",
        ["label"] = "lbl",
        ["text"] = "txt",
        ["textcanvas"] = "lbl",
        ["gallery"] = "gal",
        ["icon"] = "ico",
        ["image"] = "img",
        ["groupContainer"] = "cnt",
        ["form"] = "frm",
        ["dropdown"] = "drp",
        ["combobox"] = "cmb",
        ["datepicker"] = "dtp",
        ["checkbox"] = "chk",
        ["toggleSwitch"] = "tgl",
        ["radio"] = "rad",
        ["slider"] = "sld",
        ["htmlViewer"] = "htm",
        ["rectangle"] = "rec",
        ["timer"] = "tim",
    };

    private static readonly Dictionary<string, string> PascalPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = "Button",
        ["label"] = "Label",
        ["text"] = "Text",
        ["textcanvas"] = "Label",
        ["gallery"] = "Gallery",
        ["icon"] = "Icon",
        ["image"] = "Image",
        ["groupContainer"] = "Container",
        ["form"] = "Form",
        ["dropdown"] = "Dropdown",
        ["combobox"] = "Combo",
        ["datepicker"] = "Date",
        ["checkbox"] = "Check",
        ["toggleSwitch"] = "Toggle",
        ["radio"] = "Radio",
        ["slider"] = "Slider",
        ["htmlViewer"] = "Html",
        ["rectangle"] = "Rect",
        ["timer"] = "Timer",
    };

    public static IReadOnlyList<string> Names { get; } = ["camel-prefix", "pascal-type"];

    public static bool TryGet(string name, out NamingConfig config)
    {
        if (string.Equals(name, "camel-prefix", StringComparison.OrdinalIgnoreCase))
        {
            config = new NamingConfig
            {
                GlobalVariable = "^var[A-Z][A-Za-z0-9]*$",
                ContextVariable = "^loc[A-Z][A-Za-z0-9]*$",
                Collection = "^col[A-Z][A-Za-z0-9]*$",
                Screen = "^scr[A-Z][A-Za-z0-9]*$",
                Component = "^cmp[A-Z][A-Za-z0-9]*$",
                ControlPrefixes = CamelPrefixes,
            };
            return true;
        }

        if (string.Equals(name, "pascal-type", StringComparison.OrdinalIgnoreCase))
        {
            config = new NamingConfig
            {
                GlobalVariable = "^Var[A-Z][A-Za-z0-9]*$",
                ContextVariable = "^Loc[A-Z][A-Za-z0-9]*$",
                Collection = "^Col[A-Z][A-Za-z0-9]*$",
                Screen = "^Screen[A-Z][A-Za-z0-9]*$",
                Component = "^Component[A-Z][A-Za-z0-9]*$",
                ControlPrefixes = PascalPrefixes,
            };
            return true;
        }

        config = new NamingConfig();
        return false;
    }
}
