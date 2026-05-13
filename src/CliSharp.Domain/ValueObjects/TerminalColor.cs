namespace CliSharp.Domain.ValueObjects;

/// <summary>
/// Color of a cell: puede ser Default, Indexed (0-255) o RGB (24-bit).
/// Struct compacto de 4 bytes con value equality via record struct.
/// </summary>
public readonly record struct TerminalColor(byte ColorType, byte R, byte G, byte B)
{
    // ColorType: 0=default, 1=indexed (R=index), 2=RGB
    public bool IsDefault => ColorType == 0;
    public bool IsIndexed => ColorType == 1;
    public bool IsRgb => ColorType == 2;

    /// <summary>Índice del color (solo válido si IsIndexed).</summary>
    public byte Index => R;

    public static readonly TerminalColor Default = default;
    public static TerminalColor Indexed(byte index) => new(1, index, 0, 0);
    public static TerminalColor Rgb(byte r, byte g, byte b) => new(2, r, g, b);
}
