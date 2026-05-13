using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using CliSharp.Application.Terminal;
using CliSharp.Domain.ValueObjects;
using CliSharp.UI.Config;

namespace CliSharp.UI.Rendering;

/// <summary>
/// Optimized renderer: glyph cache, text batching by runs (enables ligatures),
/// inline images, dirty tracking.
/// </summary>
public sealed class TerminalRenderer
{
    private FontMetrics _font;

    private Color _defaultFg, _defaultBg, _cursorColor;
    private Color _hyperlinkColor = Color.Parse("#89B4FA");
    private Color[] _palette16 = new Color[16];
    private ImmutableSolidColorBrush _defaultFgBrush = null!;
    private ImmutableSolidColorBrush _defaultBgBrush = null!;
    private ImmutableSolidColorBrush[] _palette16Brushes = [];

    // Glyph cache: (char, bold, colorArgb) → FormattedText
    private readonly Dictionary<(char, bool, uint), FormattedText> _glyphCache = new();

    // Image cache: raw data → decoded Bitmap
    private readonly Dictionary<Grid.InlineImageData, Bitmap> _imageCache = new();

    // Dirty tracking
    private long _lastRenderedGeneration;

    public FontMetrics Font => _font;
    public Color Background => _defaultBg;

    public TerminalRenderer(FontMetrics? font = null)
    {
        _font = font ?? new FontMetrics();
        ApplyTheme(ThemeConfig.CatppuccinMocha());
    }

    public void ApplyTheme(ThemeConfig theme)
    {
        _defaultFg = Color.Parse(theme.Foreground);
        _defaultBg = Color.Parse(theme.Background);
        _cursorColor = Color.Parse(theme.Cursor);
        if (theme.Palette.Length >= 16)
            _palette16 = theme.Palette.Select(s => Color.Parse(s)).ToArray();
        _defaultFgBrush = new ImmutableSolidColorBrush(_defaultFg);
        _defaultBgBrush = new ImmutableSolidColorBrush(_defaultBg);
        _palette16Brushes = _palette16.Select(c => new ImmutableSolidColorBrush(c)).ToArray();
        _glyphCache.Clear(); // Invalidate cache on theme change
    }

    public void UpdateFont(FontMetrics font)
    {
        _font = font;
        _glyphCache.Clear();
    }

    public void Render(DrawingContext ctx, Grid grid)
    {
        double cw = _font.CellWidth;
        double ch = _font.CellHeight;

        // ── 1. Fondo ────────────────────────────────────────────────
        ctx.DrawRectangle(_defaultBgBrush, null,
            new Rect(0, 0, grid.Columns * cw, grid.Rows * ch));

        // ── 2. Celdas: backgrounds + texto por runs ─────────────────
        for (int row = 0; row < grid.Rows; row++)
        {
            var cells = grid.GetVisibleRow(row);
            double y = row * ch;
            int cols = Math.Min(cells.Length, grid.Columns);

            // Backgrounds
            for (int col = 0; col < cols; col++)
            {
                var (_, bg) = ResolveColors(cells[col].Attributes);
                if (bg != _defaultBg)
                    ctx.DrawRectangle(GetBrush(bg), null, new Rect(col * cw, y, cw, ch));
            }

            // Text: batching por runs de mismo (fg, bold) — enables ligatures
            int c = 0;
            while (c < cols)
            {
                var cell = cells[c];
                if (cell.Character <= ' ' || cell.Attributes.Flags.HasFlag(CellFlags.WideCharCont))
                { c++; continue; }

                var (fg, _) = ResolveColors(cell.Attributes);
                bool bold = cell.Attributes.Flags.HasFlag(CellFlags.Bold);
                var effectiveFg = cell.Attributes.HyperlinkId > 0 ? _hyperlinkColor : fg;
                int runStart = c;

                // Accumulate run of same style
                var sb = new StringBuilder();
                while (c < cols)
                {
                    var rc = cells[c];
                    if (rc.Character <= ' ') break;
                    if (rc.Attributes.Flags.HasFlag(CellFlags.WideCharCont)) { c++; continue; }

                    var (rfg, _) = ResolveColors(rc.Attributes);
                    bool rBold = rc.Attributes.Flags.HasFlag(CellFlags.Bold);
                    var rEffFg = rc.Attributes.HyperlinkId > 0 ? _hyperlinkColor : rfg;

                    if (rEffFg != effectiveFg || rBold != bold) break;
                    sb.Append(rc.Character);
                    c++;
                }

                if (sb.Length > 0)
                {
                    var typeface = bold ? _font.BoldTypeface : _font.Typeface;

                    // Single char runs: use glyph cache
                    if (sb.Length == 1)
                    {
                        var ft = GetCachedGlyph(sb[0], bold, effectiveFg);
                        ctx.DrawText(ft, new Point(runStart * cw, y));
                    }
                    else
                    {
                        // Long runs: FormattedText (Skia applies ligatures automáticamente)
                        var ft = new FormattedText(sb.ToString(),
                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            typeface, _font.FontSize, GetBrush(effectiveFg));
                        ctx.DrawText(ft, new Point(runStart * cw, y));
                    }
                }

                // Hyperlink underlines for the run
                if (cells[runStart].Attributes.HyperlinkId > 0)
                {
                    double uy = y + ch - 2;
                    ctx.DrawLine(new Pen(GetBrush(_hyperlinkColor), 1),
                        new Point(runStart * cw, uy), new Point(c * cw, uy));
                }
            }
        }

        // ── 3. Inline images ────────────────────────────────────────
        foreach (var img in grid.InlineImages)
        {
            var bmp = GetOrCreateBitmap(img);
            if (bmp is not null)
            {
                var dest = new Rect(img.Col * cw, img.Row * ch,
                    img.WidthCells * cw, img.HeightCells * ch);
                ctx.DrawImage(bmp, dest);
            }
        }

        // ── 4. Block separators (OSC 133) ───────────────────────────
        var sepBrush = new ImmutableSolidColorBrush(Color.Parse("#313244"));
        for (int row = 1; row < grid.Rows; row++)
        {
            byte flag = grid.GetVisibleRowFlag(row);
            if (flag == 0) continue;
            double sy = row * ch;
            ctx.DrawLine(new Pen(sepBrush, 1),
                new Point(8, sy - 0.5), new Point(grid.Columns * cw - 8, sy - 0.5));
            if (flag >= 2)
            {
                string badge = flag == 2 ? " \u2713 " : " \u2717 ";
                var badgeBrush = GetBrush(flag == 2 ? Color.Parse("#A6E3A1") : Color.Parse("#F38BA8"));
                var ft = new FormattedText(badge, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _font.Typeface, _font.FontSize * 0.75, badgeBrush);
                ctx.DrawText(ft, new Point(grid.Columns * cw - ft.Width - 8, sy - ch * 0.85));
            }
        }

        // ── 5. Cursor ───────────────────────────────────────────────
        if (grid.CursorVisible && grid.ViewportOffset == 0)
        {
            int curCol = Math.Min(grid.CursorColumn, grid.Columns - 1);
            ctx.DrawRectangle(
                new ImmutableSolidColorBrush(_cursorColor, 0.7), null,
                new Rect(curCol * cw, grid.CursorRow * ch, cw, ch));
        }

        _lastRenderedGeneration = grid.RenderGeneration;
    }

    // ── Glyph cache ─────────────────────────────────────────────────

    private FormattedText GetCachedGlyph(char c, bool bold, Color fg)
    {
        uint argb = ((uint)fg.A << 24) | ((uint)fg.R << 16) | ((uint)fg.G << 8) | fg.B;
        var key = (c, bold, argb);
        if (!_glyphCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(c.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                bold ? _font.BoldTypeface : _font.Typeface,
                _font.FontSize, GetBrush(fg));
            _glyphCache[key] = ft;
            if (_glyphCache.Count > 8_000) _glyphCache.Clear(); // Avoid memory leak
        }
        return ft;
    }

    // ── Image cache ─────────────────────────────────────────────────

    private Bitmap? GetOrCreateBitmap(Grid.InlineImageData img)
    {
        if (_imageCache.TryGetValue(img, out var bmp)) return bmp;
        try
        {
            using var ms = new System.IO.MemoryStream(img.ImageBytes);
            bmp = new Bitmap(ms);
            _imageCache[img] = bmp;
            if (_imageCache.Count > 100) // Limitar cache
            {
                foreach (var old in _imageCache.Values) old.Dispose();
                _imageCache.Clear();
            }
            return bmp;
        }
        catch { return null; }
    }

    // ── Color resolution ────────────────────────────────────────────

    private (Color fg, Color bg) ResolveColors(CellAttributes attrs)
    {
        var fg = ResolveTerminalColor(attrs.Foreground, true);
        var bg = ResolveTerminalColor(attrs.Background, false);
        if (attrs.Flags.HasFlag(CellFlags.Inverse)) (fg, bg) = (bg, fg);
        return (fg, bg);
    }

    private Color ResolveTerminalColor(TerminalColor color, bool isFg)
    {
        if (color.IsDefault) return isFg ? _defaultFg : _defaultBg;
        if (color.IsRgb) return Color.FromRgb(color.R, color.G, color.B);
        byte idx = color.Index;
        if (idx < 16 && idx < _palette16.Length) return _palette16[idx];
        if (idx < 232)
        {
            int c = idx - 16;
            return Color.FromRgb((byte)((c / 36) * 51), (byte)(((c / 6) % 6) * 51), (byte)((c % 6) * 51));
        }
        byte v = (byte)(8 + (idx - 232) * 10);
        return Color.FromRgb(v, v, v);
    }

    private ImmutableSolidColorBrush GetBrush(Color color)
    {
        if (color == _defaultFg) return _defaultFgBrush;
        if (color == _defaultBg) return _defaultBgBrush;
        for (int i = 0; i < _palette16.Length; i++)
            if (_palette16[i] == color) return _palette16Brushes[i];
        return new ImmutableSolidColorBrush(color);
    }
}
