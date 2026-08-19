using Microsoft.PowerFx;

namespace PpLint.PowerFx;

/// <summary>
/// Nomes que o Power Fx já conhece. Um identificador built-in nunca é uma
/// variável do app — sem essa distinção, toda chamada de função viraria
/// "variável não definida".
/// </summary>
public static class PowerFxBuiltins
{
    /// <summary>
    /// Enums do Power Fx. Vêm de lista porque o engine não os expõe numa API
    /// pública estável; em compensação mudam pouco e a lista é curta.
    /// </summary>
    private static readonly string[] Enums =
    [
        "Color", "Align", "VerticalAlign", "Font", "FontWeight", "Underline",
        "DisplayMode", "ScreenTransition", "SortOrder", "Layout", "LayoutDirection",
        "Overflow", "BorderStyle", "TextPosition", "TextMode", "TextFormat",
        "DateTimeFormat", "MatchOptions", "Match", "ErrorKind", "FormMode",
        "SubmitMode", "Direction", "Transition", "ZoomMode", "PenMode",
        "GridStyle", "ImagePosition", "LabelPosition", "Notify", "NotificationType",
        "SelectedState", "MapStyle", "Compass", "Orientation", "ExternalMessage",
    ];

    /// <summary>
    /// Funções de comportamento que o Power Apps acrescenta ao Power Fx. O
    /// engine core não as conhece — quem as registra é o host —, então
    /// GetAllFunctionNames() não as devolve e elas precisam vir daqui.
    /// </summary>
    private static readonly string[] BehaviorFunctions =
    [
        "Set", "UpdateContext", "Navigate", "Back", "Exit", "Launch",
        "Collect", "ClearCollect", "Clear", "Remove", "RemoveIf", "Patch", "Update", "UpdateIf",
        "Notify", "SubmitForm", "ResetForm", "EditForm", "NewForm", "ViewForm",
        "Reset", "ResetIf", "SetFocus", "Select", "Refresh", "Revert",
        "Print", "RequestHide", "SaveData", "LoadData", "ClearData",
        "Concurrent", "Trace", "EnableInput", "DisableInput",
        "UpdateContextIf", "ShowColumns", "RenameColumns", "DropColumns", "AddColumns",
    ];

    /// <summary>
    /// Valores de enum que o Power Apps aceita sem qualificar o enum:
    /// Navigate(scr, Fade) e SortByColumns(t, "c", Descending) são código
    /// corrente. Sem eles, cada um vira "nome nunca definido".
    /// </summary>
    private static readonly string[] EnumValues =
    [
        // ScreenTransition
        "Fade", "Cover", "UnCover", "CoverRight", "UnCoverRight",
        // SortOrder
        "Ascending", "Descending",
        // FormMode
        "Edit", "New", "View",
        // Align / VerticalAlign
        "Center", "Justify", "Start", "End", "Top", "Bottom", "Middle",
        // FontWeight / Underline
        "Bold", "Semibold", "Lighter", "Strikethrough",
        // DisplayMode
        "Disabled", "Edit", "View",
        // NotificationType
        "Success", "Warning", "Information",
        // ImagePosition / Layout
        "Fill", "Fit", "Stretch", "Tile", "Horizontal", "Vertical",
        // BorderStyle
        "Solid", "Dashed", "Dotted",
        // genéricos
        "None", "Auto", "Scroll", "Hidden", "Normal",
    ];

    static PowerFxBuiltins()
    {
        var engine = new Engine(new PowerFxConfig());

        FunctionNames = engine.GetAllFunctionNames()
            .Concat(BehaviorFunctions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        EnumNames = Enums
            .Concat(EnumValues)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> FunctionNames { get; }

    public static IReadOnlySet<string> EnumNames { get; }

    public static bool IsBuiltin(string name) =>
        FunctionNames.Contains(name) || EnumNames.Contains(name);
}
