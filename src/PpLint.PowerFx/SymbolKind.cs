namespace PpLint.PowerFx;

/// <summary>
/// O que um identificador é. Unknown é o único que permite tratá-lo como
/// variável do app — todo o resto tem dono conhecido.
/// </summary>
public enum SymbolKind
{
    Unknown,
    Control,
    Screen,
    DataSource,
    Function,
    Enum,
    RowScope,
}
