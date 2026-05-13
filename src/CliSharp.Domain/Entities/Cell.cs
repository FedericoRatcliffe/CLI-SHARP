using CliSharp.Domain.ValueObjects;

namespace CliSharp.Domain.Entities;

/// <summary>
/// A single cell in the terminal grid: carácter + atributos visuales.
/// Struct para layout compacto en memoria (~6 bytes por celda).
/// </summary>
public record struct Cell(char Character, CellAttributes Attributes)
{
    public static readonly Cell Empty = new(' ', CellAttributes.Default);
}
