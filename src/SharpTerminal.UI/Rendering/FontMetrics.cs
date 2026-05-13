using System.Globalization;
using Avalonia.Media;

namespace SharpTerminal.UI.Rendering;

/// <summary>
/// Loads a monospaced font and measures cell dimensions (CellWidth × CellHeight).
/// All renderer position calculations use these metrics.
/// </summary>
public sealed class FontMetrics
{
    public FontFamily FontFamily { get; }
    public Typeface Typeface { get; }
    public Typeface BoldTypeface { get; }
    public double FontSize { get; }
    public double CellWidth { get; }
    public double CellHeight { get; }

    public FontMetrics(string fontFamily = "Cascadia Code,Consolas,Courier New,monospace", double fontSize = 14)
    {
        FontFamily = new FontFamily(fontFamily);
        FontSize = fontSize;
        Typeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Normal);
        BoldTypeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold);

        // Measure with a reference string for better precision
        var measure = new FormattedText(
            "MMMMMMMMMM",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            fontSize,
            Brushes.White);

        CellWidth = measure.Width / 10.0;
        CellHeight = measure.Height;
    }
}
