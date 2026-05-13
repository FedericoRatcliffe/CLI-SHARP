namespace SharpTerminal.Domain.Enums;

/// <summary>
/// The 16 standard ANSI colors estándar + Default.
/// Los valores 0-7 mapean directo a SGR 30-37 (fg) y 40-47 (bg).
/// Los valores 8-15 mapean a SGR 90-97 (bright fg) y 100-107 (bright bg).
/// </summary>
public enum AnsiColor : byte
{
    Black = 0,
    Red = 1,
    Green = 2,
    Yellow = 3,
    Blue = 4,
    Magenta = 5,
    Cyan = 6,
    White = 7,
    BrightBlack = 8,
    BrightRed = 9,
    BrightGreen = 10,
    BrightYellow = 11,
    BrightBlue = 12,
    BrightMagenta = 13,
    BrightCyan = 14,
    BrightWhite = 15,
    Default = 255,
}
