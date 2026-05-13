using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CliSharp.Application.Terminal;
using CliSharp.UI.Rendering;
using TerminalGrid = CliSharp.Application.Terminal.Grid;

namespace CliSharp.UI.Controls;

/// <summary>
/// Control custom: renderiza Grid, captura teclado, mouse wheel/tracking,
/// bracketed paste, hyperlink Ctrl+Click.
/// </summary>
public class TerminalCanvas : Control
{
    private TerminalGrid? _grid;
    private object? _syncRoot;
    private readonly TerminalRenderer _renderer;

    public Func<byte[], Task>? SendInput { get; set; }
    public TerminalRenderer Renderer => _renderer;
    public TerminalGrid? Grid => _grid;

    public TerminalCanvas()
    {
        _renderer = new TerminalRenderer();
        ClipToBounds = true;
        Focusable = true;
    }

    public void SetGrid(TerminalGrid grid, object? syncRoot = null)
    {
        _grid = grid; _syncRoot = syncRoot;
        InvalidateMeasure(); InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_grid is null) return default;
        double w = double.IsInfinity(availableSize.Width) ? _grid.Columns * _renderer.Font.CellWidth : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? _grid.Rows * _renderer.Font.CellHeight : availableSize.Height;
        return new Size(w, h);
    }

    public override void Render(DrawingContext context)
    {
        if (_grid is null) return;
        if (_syncRoot is not null) lock (_syncRoot) _renderer.Render(context, _grid);
        else _renderer.Render(context, _grid);
    }

    // ── Mouse wheel: scrollback o mouse tracking ────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (_grid is null) { base.OnPointerWheelChanged(e); return; }

        if (_grid.MouseTrackingMode > 0 && SendInput is not null)
        {
            int button = e.Delta.Y > 0 ? 64 : 65;
            SendMouseSequence(e.GetPosition(this), button, 'M');
        }
        else if (_syncRoot is not null)
        {
            lock (_syncRoot) _grid.ScrollViewport(e.Delta.Y > 0 ? 3 : -3);
            InvalidateVisual();
        }
        e.Handled = true;
    }

    // ── Mouse press/release/move: tracking + hyperlink click ────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        if (_grid is null) { base.OnPointerPressed(e); return; }

        var props = e.GetCurrentPoint(this).Properties;

        // Ctrl+Click: hyperlink
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && props.IsLeftButtonPressed)
        {
            var pos = e.GetPosition(this);
            int col = Math.Clamp((int)(pos.X / _renderer.Font.CellWidth), 0, _grid.Columns - 1);
            int row = Math.Clamp((int)(pos.Y / _renderer.Font.CellHeight), 0, _grid.Rows - 1);
            // 1. Explicit OSC 8 hyperlinks
            var cell = _grid[row, col];
            if (cell.Attributes.HyperlinkId > 0)
            {
                var uri = _grid.GetHyperlinkUri(cell.Attributes.HyperlinkId);
                if (uri is not null && (uri.StartsWith("http://") || uri.StartsWith("https://")))
                {
                    try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
                    e.Handled = true; return;
                }
            }

            // 2. Automatic URL and file path detection
            var link = LinkDetector.DetectAt(_grid, row, col);
            if (link is not null)
            {
                try
                {
                    if (link.Type == LinkDetector.LinkType.Url)
                        Process.Start(new ProcessStartInfo(link.Uri) { UseShellExecute = true });
                    else // FilePath → abrir en VS Code
                        Process.Start("code", $"--goto \"{link.Uri}\"");
                }
                catch { }
                e.Handled = true; return;
            }
        }

        if (_grid.MouseTrackingMode > 0 && SendInput is not null)
        {
            int button = props.IsMiddleButtonPressed ? 1 : props.IsRightButtonPressed ? 2 : 0;
            SendMouseSequence(e.GetPosition(this), button, 'M');
            e.Handled = true;
        }
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_grid?.MouseTrackingMode > 0 && SendInput is not null)
        {
            SendMouseSequence(e.GetPosition(this), 0, 'm');
            e.Handled = true;
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_grid is null || SendInput is null) { base.OnPointerMoved(e); return; }

        bool shouldReport = _grid.MouseTrackingMode >= 1003 ||
            (_grid.MouseTrackingMode >= 1002 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed);

        if (shouldReport)
        {
            SendMouseSequence(e.GetPosition(this), 32, 'M'); // 32 = motion
            e.Handled = true;
        }
        base.OnPointerMoved(e);
    }

    private void SendMouseSequence(Point pos, int button, char suffix)
    {
        if (SendInput is null || _grid is null) return;
        int col = Math.Clamp((int)(pos.X / _renderer.Font.CellWidth) + 1, 1, _grid.Columns);
        int row = Math.Clamp((int)(pos.Y / _renderer.Font.CellHeight) + 1, 1, _grid.Rows);

        if (_grid.SgrMouseMode)
            _ = SendInput(Encoding.UTF8.GetBytes($"\u001b[<{button};{col};{row}{suffix}"));
        else
            _ = SendInput([(byte)'\u001b', (byte)'[', (byte)'M',
                (byte)(button + 32), (byte)(col + 32), (byte)(row + 32)]);
    }

    // ── Keyboard: TextInput + KeyDown ───────────────────────────────

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (SendInput is not null && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            _ = SendInput(Encoding.UTF8.GetBytes(e.Text));
            e.Handled = true;
        }
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
            if (e.Key >= Key.A && e.Key <= Key.Z) data = [(byte)(e.Key - Key.A + 1)];
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

        if (data is not null) { _ = SendInput(data); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // ── Clipboard paste (con bracketed paste mode) ──────────────────

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
    }
}
