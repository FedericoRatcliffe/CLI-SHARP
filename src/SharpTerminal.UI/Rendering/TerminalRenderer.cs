using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using SharpTerminal.Application.Terminal;
using SharpTerminal.Domain.ValueObjects;
using SharpTerminal.UI.Config;

namespace SharpTerminal.UI.Rendering;

/// <summary>
/// Optimized renderer: StringBuilder reuse, background rect batching, glyph cache,
/// text run batching, cursor shapes (block/bar/underline), underline/strikethrough,
/// selection, search highlights, inline images, command blocks, visual bell.
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

    private static readonly Color SelectionColor = Color.FromArgb(80, 137, 180, 250);
    private static readonly Color SearchColor = Color.FromArgb(100, 249, 226, 175);
    private static readonly Color ActiveSearchColor = Color.FromArgb(160, 249, 226, 175);

    private readonly Dictionary<(char, bool, uint), FormattedText> _glyphCache = new();
    private readonly Dictionary<Grid.InlineImageData, Bitmap> _imageCache = new();
    private readonly StringBuilder _runBuffer = new(256); // Reused per render

    // State set by TerminalCanvas before each Render()
    public bool CursorBlinkVisible { get; set; } = true;
    public bool BellFlash { get; set; }
    public (int SR, int SC, int ER, int EC)? Selection { get; set; }
    public List<(int ViewRow, int Col, int Len, bool Active)>? SearchHighlights { get; set; }

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
        _glyphCache.Clear();
    }

    public void UpdateFont(FontMetrics font) { _font = font; _glyphCache.Clear(); }

    public void Render(DrawingContext ctx, Grid grid)
    {
        double cw = _font.CellWidth;
        double ch = _font.CellHeight;
        int gridCols = grid.Columns;

        // 1. Background fill
        ctx.DrawRectangle(_defaultBgBrush, null, new Rect(0, 0, gridCols * cw, grid.Rows * ch));

        // 2. Cell backgrounds (batched: consecutive same-color → 1 rect) + text runs
        for (int row = 0; row < grid.Rows; row++)
        {
            var cells = grid.GetVisibleRow(row);
            double y = row * ch;
            int cols = Math.Min(cells.Length, gridCols);

            // -- Batched backgrounds --
            int bgStart = -1;
            Color bgColor = default;
            for (int col = 0; col <= cols; col++)
            {
                Color cellBg = col < cols ? ResolveColors(cells[col].Attributes).bg : _defaultBg;
                if (cellBg == bgColor && col < cols) continue;

                // Flush previous bg run
                if (bgStart >= 0 && bgColor != _defaultBg)
                    ctx.DrawRectangle(GetBrush(bgColor), null,
                        new Rect(bgStart * cw, y, (col - bgStart) * cw, ch));

                bgStart = col;
                bgColor = cellBg;
            }

            // -- Text runs (batched by same fg+bold) --
            int c = 0;
            while (c < cols)
            {
                var cell = cells[c];
                if (cell.Character <= ' ' || cell.Attributes.Flags.HasFlag(CellFlags.WideCharCont)) { c++; continue; }

                var (fg, _) = ResolveColors(cell.Attributes);
                bool bold = cell.Attributes.Flags.HasFlag(CellFlags.Bold);
                var effectiveFg = cell.Attributes.HyperlinkId > 0 ? _hyperlinkColor : fg;
                int runStart = c;
                CellFlags runFlags = cell.Attributes.Flags;
                _runBuffer.Clear();

                while (c < cols)
                {
                    var rc = cells[c];
                    if (rc.Character <= ' ') break;
                    if (rc.Attributes.Flags.HasFlag(CellFlags.WideCharCont)) { c++; continue; }
                    var (rfg, _) = ResolveColors(rc.Attributes);
                    bool rBold = rc.Attributes.Flags.HasFlag(CellFlags.Bold);
                    var rEffFg = rc.Attributes.HyperlinkId > 0 ? _hyperlinkColor : rfg;
                    if (rEffFg != effectiveFg || rBold != bold) break;
                    _runBuffer.Append(rc.Character);
                    c++;
                }

                if (_runBuffer.Length > 0)
                {
                    if (_runBuffer.Length == 1)
                    {
                        ctx.DrawText(GetCachedGlyph(_runBuffer[0], bold, effectiveFg), new Point(runStart * cw, y));
                    }
                    else
                    {
                        var typeface = bold ? _font.BoldTypeface : _font.Typeface;
                        var ft = new FormattedText(_runBuffer.ToString(), CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, typeface, _font.FontSize, GetBrush(effectiveFg));
                        ctx.DrawText(ft, new Point(runStart * cw, y));
                    }
                }

                // Underline / strikethrough / hyperlink decorations for the run
                double runEndX = c * cw;
                double runStartX = runStart * cw;

                if (runFlags.HasFlag(CellFlags.Underline) || cells[runStart].Attributes.HyperlinkId > 0)
                {
                    var pen = new Pen(GetBrush(cells[runStart].Attributes.HyperlinkId > 0 ? _hyperlinkColor : effectiveFg), 1);
                    ctx.DrawLine(pen, new Point(runStartX, y + ch - 2), new Point(runEndX, y + ch - 2));
                }
                if (runFlags.HasFlag(CellFlags.Strikethrough))
                {
                    var pen = new Pen(GetBrush(effectiveFg), 1);
                    ctx.DrawLine(pen, new Point(runStartX, y + ch * 0.5), new Point(runEndX, y + ch * 0.5));
                }
            }
        }

        // 3. Selection highlight
        if (Selection is var (sr, sc, er, ec))
        {
            var selBrush = new ImmutableSolidColorBrush(SelectionColor);
            for (int row = sr; row <= er && row < grid.Rows; row++)
            {
                int s = row == sr ? sc : 0;
                int e = row == er ? ec : gridCols - 1;
                ctx.DrawRectangle(selBrush, null, new Rect(s * cw, row * ch, (e - s + 1) * cw, ch));
            }
        }

        // 4. Search highlights
        if (SearchHighlights is not null)
            foreach (var (vr, col, len, active) in SearchHighlights)
                ctx.DrawRectangle(new ImmutableSolidColorBrush(active ? ActiveSearchColor : SearchColor),
                    null, new Rect(col * cw, vr * ch, len * cw, ch));

        // 5. Inline images
        foreach (var img in grid.InlineImages)
        {
            var bmp = GetOrCreateBitmap(img);
            if (bmp is not null)
                ctx.DrawImage(bmp, new Rect(img.Col * cw, img.Row * ch, img.WidthCells * cw, img.HeightCells * ch));
        }

        // 6. Block separators (OSC 133)
        var sepBrush = new ImmutableSolidColorBrush(Color.Parse("#313244"));
        for (int row = 1; row < grid.Rows; row++)
        {
            byte flag = grid.GetVisibleRowFlag(row);
            if (flag == 0) continue;
            double sy = row * ch;
            ctx.DrawLine(new Pen(sepBrush, 1), new Point(8, sy - 0.5), new Point(gridCols * cw - 8, sy - 0.5));
            if (flag >= 2)
            {
                string badge = flag == 2 ? " \u2713 " : " \u2717 ";
                var badgeBrush = GetBrush(flag == 2 ? Color.Parse("#A6E3A1") : Color.Parse("#F38BA8"));
                var ft = new FormattedText(badge, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _font.Typeface, _font.FontSize * 0.75, badgeBrush);
                ctx.DrawText(ft, new Point(gridCols * cw - ft.Width - 8, sy - ch * 0.85));
            }
        }

        // 7. Cursor (block / bar / underline, with blink)
        if (grid.CursorVisible && grid.ViewportOffset == 0 && CursorBlinkVisible)
        {
            int curCol = Math.Min(grid.CursorColumn, gridCols - 1);
            double cx = curCol * cw, cy = grid.CursorRow * ch;
            var curBrush = new ImmutableSolidColorBrush(_cursorColor, 0.8);

            switch (grid.CursorShape)
            {
                case 0: case 1: case 2: // Block
                    ctx.DrawRectangle(curBrush, null, new Rect(cx, cy, cw, ch));
                    break;
                case 3: case 4: // Underline
                    ctx.DrawRectangle(curBrush, null, new Rect(cx, cy + ch - 3, cw, 3));
                    break;
                case 5: case 6: // Bar (I-beam)
                    ctx.DrawRectangle(curBrush, null, new Rect(cx, cy, 2, ch));
                    break;
            }
        }

        // 8. Visual bell flash
        if (BellFlash)
            ctx.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                null, new Rect(0, 0, gridCols * cw, grid.Rows * ch));
    }

    // ── Glyph cache ─────────────────────────────────────────────────

    private FormattedText GetCachedGlyph(char c, bool bold, Color fg)
    {
        uint argb = ((uint)fg.A << 24) | ((uint)fg.R << 16) | ((uint)fg.G << 8) | fg.B;
        var key = (c, bold, argb);
        if (!_glyphCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                bold ? _font.BoldTypeface : _font.Typeface, _font.FontSize, GetBrush(fg));
            _glyphCache[key] = ft;
            if (_glyphCache.Count > 8_000) _glyphCache.Clear();
        }
        return ft;
    }

    private Bitmap? GetOrCreateBitmap(Grid.InlineImageData img)
    {
        if (_imageCache.TryGetValue(img, out var bmp)) return bmp;
        try
        {
            using var ms = new System.IO.MemoryStream(img.ImageBytes);
            bmp = new Bitmap(ms);
            _imageCache[img] = bmp;
            if (_imageCache.Count > 100) { foreach (var old in _imageCache.Values) old.Dispose(); _imageCache.Clear(); }
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
        if (idx < 232) { int c = idx - 16; return Color.FromRgb((byte)((c / 36) * 51), (byte)(((c / 6) % 6) * 51), (byte)((c % 6) * 51)); }
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
