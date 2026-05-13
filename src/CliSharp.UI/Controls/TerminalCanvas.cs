using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CliSharp.Application.Terminal;
using CliSharp.Domain.ValueObjects;
using CliSharp.UI.Rendering;
using TerminalGrid = CliSharp.Application.Terminal.Grid;
using TerminalFontMetrics = CliSharp.UI.Rendering.FontMetrics;

namespace CliSharp.UI.Controls;

/// <summary>
/// Terminal control: rendering, keyboard, mouse, selection, cursor blink, search highlights.
/// </summary>
public class TerminalCanvas : Control
{
    private TerminalGrid? _grid;
    private object? _syncRoot;
    private readonly TerminalRenderer _renderer;

    // Selection state
    private (int Row, int Col)? _selStart, _selEnd;
    private bool _selecting;

    // Cursor blink
    private readonly DispatcherTimer _blinkTimer;
    private bool _cursorVisible = true;
    private long _lastGridGen;

    // Visual bell
    private bool _bellFlash;

    // Render throttle (~60fps max)
    private DateTime _lastRenderTime;
    private bool _renderPending;

    // Search
    private List<(int AbsRow, int Col, int Len)>? _searchMatches;
    private int _activeMatch = -1;
    private string? _searchQuery;

    public Func<byte[], Task>? SendInput { get; set; }
    public TerminalRenderer Renderer => _renderer;
    public TerminalGrid? Grid => _grid;

    public TerminalCanvas()
    {
        _renderer = new TerminalRenderer();
        ClipToBounds = true;
        Focusable = true;

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) => { _cursorVisible = !_cursorVisible; InvalidateVisual(); };
        _blinkTimer.Start();
    }

    public void SetGrid(TerminalGrid grid, object? syncRoot = null)
    {
        _grid = grid; _syncRoot = syncRoot;
        InvalidateMeasure(); InvalidateVisual();
    }

    /// <summary>Request a render, throttled to ~60fps.</summary>
    public void RequestRender()
    {
        if (_renderPending) return;
        var elapsed = (DateTime.UtcNow - _lastRenderTime).TotalMilliseconds;
        if (elapsed >= 16)
        {
            _lastRenderTime = DateTime.UtcNow;
            InvalidateVisual();
        }
        else
        {
            _renderPending = true;
            DispatcherTimer.RunOnce(() =>
            {
                _renderPending = false;
                _lastRenderTime = DateTime.UtcNow;
                InvalidateVisual();
            }, TimeSpan.FromMilliseconds(16 - elapsed));
        }
    }

    /// <summary>Change font size (for Ctrl+/Ctrl- zoom).</summary>
    public void AdjustFontSize(double delta)
    {
        double newSize = Math.Clamp(_renderer.Font.FontSize + delta, 8, 36);
        _renderer.UpdateFont(new TerminalFontMetrics(_renderer.Font.FontFamily.Name, newSize));
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Triggers a brief visual bell flash.</summary>
    public void FlashBell()
    {
        _bellFlash = true;
        InvalidateVisual();
        DispatcherTimer.RunOnce(() => { _bellFlash = false; InvalidateVisual(); }, TimeSpan.FromMilliseconds(120));
    }

    private void ResetBlink()
    {
        _cursorVisible = true;
        _blinkTimer.Stop();
        _blinkTimer.Start();
    }

    private (int Row, int Col) PixelToCell(Point pos)
    {
        int col = Math.Clamp((int)(pos.X / _renderer.Font.CellWidth), 0, (_grid?.Columns ?? 1) - 1);
        int row = Math.Clamp((int)(pos.Y / _renderer.Font.CellHeight), 0, (_grid?.Rows ?? 1) - 1);
        return (row, col);
    }

    // ── Selection ───────────────────────────────────────────────────

    public bool HasSelection => _selStart is not null && _selEnd is not null && _selStart != _selEnd;

    public string GetSelectedText()
    {
        if (_grid is null || !HasSelection) return "";
        var (sr, sc) = _selStart!.Value;
        var (er, ec) = _selEnd!.Value;
        if (sr > er || (sr == er && sc > ec)) ((sr, sc), (er, ec)) = ((er, ec), (sr, sc));

        var sb = new StringBuilder();
        for (int r = sr; r <= er; r++)
        {
            var cells = _grid.GetVisibleRow(r);
            int s = r == sr ? sc : 0;
            int e = r == er ? ec : _grid.Columns - 1;
            for (int c = s; c <= e && c < cells.Length; c++)
                if (!cells[c].Attributes.Flags.HasFlag(CellFlags.WideCharCont))
                    sb.Append(cells[c].Character);
            if (r < er) sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    public void ClearSelection() { _selStart = null; _selEnd = null; InvalidateVisual(); }

    public async Task CopySelectionAsync()
    {
        if (!HasSelection) return;
        var text = GetSelectedText();
        if (string.IsNullOrEmpty(text)) return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is not null) await clip.SetTextAsync(text);
        ClearSelection();
    }

    // ── Search ──────────────────────────────────────────────────────

    public void SetSearch(string? query)
    {
        _searchQuery = query;
        if (_grid is null || string.IsNullOrEmpty(query))
        {
            _searchMatches = null; _activeMatch = -1;
            InvalidateVisual();
            return;
        }
        lock (_syncRoot ?? new object())
        {
            _searchMatches = _grid.Search(query);
        }
        _activeMatch = _searchMatches.Count > 0 ? 0 : -1;
        ScrollToActiveMatch();
        InvalidateVisual();
    }

    public int SearchMatchCount => _searchMatches?.Count ?? 0;
    public int ActiveMatchIndex => _activeMatch;

    public void NextMatch()
    {
        if (_searchMatches is null || _searchMatches.Count == 0) return;
        _activeMatch = (_activeMatch + 1) % _searchMatches.Count;
        ScrollToActiveMatch();
        InvalidateVisual();
    }

    public void PrevMatch()
    {
        if (_searchMatches is null || _searchMatches.Count == 0) return;
        _activeMatch = (_activeMatch - 1 + _searchMatches.Count) % _searchMatches.Count;
        ScrollToActiveMatch();
        InvalidateVisual();
    }

    private void ScrollToActiveMatch()
    {
        if (_grid is null || _searchMatches is null || _activeMatch < 0) return;
        var match = _searchMatches[_activeMatch];
        lock (_syncRoot ?? new object())
        {
            _grid.ScrollToAbsoluteRow(match.AbsRow);
        }
    }

    // ── Layout ──────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_grid is null) return default;
        double w = double.IsInfinity(availableSize.Width) ? _grid.Columns * _renderer.Font.CellWidth : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? _grid.Rows * _renderer.Font.CellHeight : availableSize.Height;
        return new Size(w, h);
    }

    // ── Render ───────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        if (_grid is null) return;

        // Reset blink when grid content changes
        if (_grid.RenderGeneration != _lastGridGen)
        {
            _lastGridGen = _grid.RenderGeneration;
            _cursorVisible = true;
        }

        // Pass state to renderer
        _renderer.CursorBlinkVisible = _cursorVisible;
        _renderer.BellFlash = _bellFlash;
        _renderer.Selection = HasSelection ? NormalizeSelection() : null;

        // Search matches visible in current viewport
        _renderer.SearchHighlights = null;
        if (_searchMatches is not null && _grid is not null)
        {
            var visible = new List<(int ViewRow, int Col, int Len, bool Active)>();
            for (int i = 0; i < _searchMatches.Count; i++)
            {
                var m = _searchMatches[i];
                int viewRow = m.AbsRow - _grid.ViewRowToAbsolute(0);
                if (viewRow >= 0 && viewRow < _grid.Rows)
                    visible.Add((viewRow, m.Col, m.Len, i == _activeMatch));
            }
            if (visible.Count > 0) _renderer.SearchHighlights = visible;
        }

        if (_syncRoot is not null) lock (_syncRoot) _renderer.Render(context, _grid!);
        else _renderer.Render(context, _grid!);
    }

    private (int SR, int SC, int ER, int EC)? NormalizeSelection()
    {
        if (_selStart is null || _selEnd is null) return null;
        var (sr, sc) = _selStart.Value;
        var (er, ec) = _selEnd.Value;
        if (sr > er || (sr == er && sc > ec)) ((sr, sc), (er, ec)) = ((er, ec), (sr, sc));
        return (sr, sc, er, ec);
    }

    // ── Mouse: selection + wheel + tracking ─────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        if (_grid is null) { base.OnPointerPressed(e); return; }
        var props = e.GetCurrentPoint(this).Properties;

        // Ctrl+Click: hyperlinks
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && props.IsLeftButtonPressed)
        {
            var (row, col) = PixelToCell(e.GetPosition(this));
            var cell = _grid[row, col];
            if (cell.Attributes.HyperlinkId > 0)
            {
                var uri = _grid.GetHyperlinkUri(cell.Attributes.HyperlinkId);
                if (uri is not null && (uri.StartsWith("http://") || uri.StartsWith("https://")))
                { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { } e.Handled = true; return; }
            }
            var link = LinkDetector.DetectAt(_grid, row, col);
            if (link is not null)
            {
                try
                {
                    if (link.Type == LinkDetector.LinkType.Url) Process.Start(new ProcessStartInfo(link.Uri) { UseShellExecute = true });
                    else Process.Start("code", $"--goto \"{link.Uri}\"");
                }
                catch { }
                e.Handled = true; return;
            }
        }

        // Right-click or middle-click paste
        if ((props.IsRightButtonPressed || props.IsMiddleButtonPressed) && _grid.MouseTrackingMode == 0)
        { _ = PasteAsync(); e.Handled = true; return; }

        // Left-click: start selection (when not mouse tracking)
        if (props.IsLeftButtonPressed && _grid.MouseTrackingMode == 0)
        {
            _selStart = PixelToCell(e.GetPosition(this));
            _selEnd = _selStart;
            _selecting = true;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Mouse tracking mode
        if (_grid.MouseTrackingMode > 0 && SendInput is not null)
        {
            int button = props.IsMiddleButtonPressed ? 1 : props.IsRightButtonPressed ? 2 : 0;
            SendMouseSequence(e.GetPosition(this), button, 'M');
            e.Handled = true;
        }
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_grid is null) { base.OnPointerMoved(e); return; }

        // Selection drag
        if (_selecting)
        {
            _selEnd = PixelToCell(e.GetPosition(this));
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Mouse tracking
        if (SendInput is not null)
        {
            bool shouldReport = _grid.MouseTrackingMode >= 1003 ||
                (_grid.MouseTrackingMode >= 1002 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
            if (shouldReport) { SendMouseSequence(e.GetPosition(this), 32, 'M'); e.Handled = true; }
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_selecting)
        {
            _selecting = false;
            if (_selStart == _selEnd) ClearSelection(); // Click without drag
            e.Handled = true;
            return;
        }
        if (_grid?.MouseTrackingMode > 0 && SendInput is not null)
        { SendMouseSequence(e.GetPosition(this), 0, 'm'); e.Handled = true; }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (_grid is null) { base.OnPointerWheelChanged(e); return; }
        if (_grid.MouseTrackingMode > 0 && SendInput is not null)
        { SendMouseSequence(e.GetPosition(this), e.Delta.Y > 0 ? 64 : 65, 'M'); }
        else if (_syncRoot is not null)
        { lock (_syncRoot) _grid.ScrollViewport(e.Delta.Y > 0 ? 1 : -1); InvalidateVisual(); }
        e.Handled = true;
    }

    private void SendMouseSequence(Point pos, int button, char suffix)
    {
        if (SendInput is null || _grid is null) return;
        int col = Math.Clamp((int)(pos.X / _renderer.Font.CellWidth) + 1, 1, _grid.Columns);
        int row = Math.Clamp((int)(pos.Y / _renderer.Font.CellHeight) + 1, 1, _grid.Rows);
        if (_grid.SgrMouseMode) _ = SendInput(Encoding.UTF8.GetBytes($"\u001b[<{button};{col};{row}{suffix}"));
        else _ = SendInput([(byte)'\u001b', (byte)'[', (byte)'M', (byte)(button + 32), (byte)(col + 32), (byte)(row + 32)]);
    }

    // ── Keyboard ────────────────────────────────────────────────────

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (SendInput is not null && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        { _ = SendInput(Encoding.UTF8.GetBytes(e.Text)); e.Handled = true; ResetBlink(); }
        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (SendInput is null) { base.OnKeyDown(e); return; }
        byte[]? data = null;
        bool appCursor = _grid?.ApplicationCursorKeys == true;

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key == Key.V) { _ = PasteAsync(); e.Handled = true; base.OnKeyDown(e); return; }

            // Ctrl+C: copy if selection, otherwise send interrupt
            if (e.Key == Key.C && HasSelection)
            { _ = CopySelectionAsync(); e.Handled = true; base.OnKeyDown(e); return; }

            if (e.Key >= Key.A && e.Key <= Key.Z) data = [(byte)(e.Key - Key.A + 1)];
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            if (e.Key == Key.C && HasSelection)
            { _ = CopySelectionAsync(); e.Handled = true; base.OnKeyDown(e); return; }
        }
        else if (e.KeyModifiers == KeyModifiers.None)
        {
            byte a = appCursor ? (byte)'O' : (byte)'[';
            data = e.Key switch
            {
                Key.Enter => [0x0D], Key.Back => [0x7F], Key.Tab => [0x09], Key.Escape => [0x1B],
                Key.Up => [0x1B, a, (byte)'A'], Key.Down => [0x1B, a, (byte)'B'],
                Key.Right => [0x1B, a, (byte)'C'], Key.Left => [0x1B, a, (byte)'D'],
                Key.Home => [0x1B, a, (byte)'H'], Key.End => [0x1B, a, (byte)'F'],
                Key.Delete => [0x1B, (byte)'[', (byte)'3', (byte)'~'],
                Key.PageUp => [0x1B, (byte)'[', (byte)'5', (byte)'~'],
                Key.PageDown => [0x1B, (byte)'[', (byte)'6', (byte)'~'],
                _ => null
            };
        }

        if (data is not null)
        {
            ClearSelection(); // Any keystroke clears selection
            _ = SendInput(data);
            e.Handled = true;
            ResetBlink();
        }
        base.OnKeyDown(e);
    }

    // ── Paste ────────────────────────────────────────────────────────

    private async Task PasteAsync()
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null || SendInput is null) return;
        var text = await clip.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        var payload = Encoding.UTF8.GetBytes(text);
        if (_grid?.BracketedPasteMode == true)
        {
            byte[] prefix = "\u001b[200~"u8.ToArray(), suffix = "\u001b[201~"u8.ToArray();
            var wrapped = new byte[prefix.Length + payload.Length + suffix.Length];
            prefix.CopyTo(wrapped, 0); payload.CopyTo(wrapped, prefix.Length); suffix.CopyTo(wrapped, prefix.Length + payload.Length);
            await SendInput(wrapped);
        }
        else await SendInput(payload);
        ResetBlink();
    }
}
