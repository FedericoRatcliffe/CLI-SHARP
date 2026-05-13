namespace SharpTerminal.Domain.ValueObjects;

[Flags]
public enum CellFlags : byte
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Inverse = 1 << 3,
    Hidden = 1 << 4,
    Strikethrough = 1 << 5,
    WideCharCont = 1 << 6, // This cell is the right half of a carácter wide (CJK)
}

/// <summary>
/// Visual attributes of a cell: colores (16/256/RGB), flags de estilo, hyperlink.
/// </summary>
public record struct CellAttributes(
    TerminalColor Foreground,
    TerminalColor Background,
    CellFlags Flags,
    ushort HyperlinkId = 0)
{
    public static readonly CellAttributes Default = new(TerminalColor.Default, TerminalColor.Default, CellFlags.None, 0);
}
