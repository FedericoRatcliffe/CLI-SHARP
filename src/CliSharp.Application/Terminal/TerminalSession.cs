using System.Diagnostics;
using System.IO;
using System.Text;
using CliSharp.Application.Abstractions;
using CliSharp.Application.Parser;

namespace CliSharp.Application.Terminal;

/// <summary>
/// Orchestrator: connects PTY ↔ Parser ↔ GridManager ↔ Grid.
/// Reads PTY output in background, decodes UTF-8, feeds the parser.
///
/// Threading:
///   - ReadOutputLoopAsync runs in background, takes lock(SyncRoot) to parse/update grid
///   - El renderer (UI thread) toma lock(SyncRoot) to read the grid
///   - OutputReceived fires on background thread; caller must dispatch to UI
/// </summary>
public sealed class TerminalSession : IAsyncDisposable
{
    private readonly Grid _grid;
    private readonly GridManager _gridManager;
    private readonly AnsiParser _parser;
    private readonly IPtyConnection _pty;
    private readonly CancellationTokenSource _cts = new();
    private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();

    public Grid Grid => _grid;
    public object SyncRoot { get; } = new();

    public event Action? OutputReceived;
    public event Action? ProcessExited;
    public event Action<string>? TitleChanged;
    public event Action<string>? ClipboardSetRequested;

    public TerminalSession(IPtyConnection pty, int columns, int rows)
    {
        _pty = pty;
        _grid = new Grid(columns, rows);
        _gridManager = new GridManager(_grid);
        _parser = new AnsiParser(_gridManager);
        _pty.ProcessExited += () => ProcessExited?.Invoke();
        _grid.TitleChanged += title => TitleChanged?.Invoke(title);
        _grid.ClipboardSetRequested += text => ClipboardSetRequested?.Invoke(text);
        _grid.DirectoryChanged += dir => _ = UpdateTabTitleWithGitAsync(dir);
    }

    private async Task UpdateTabTitleWithGitAsync(string dir)
    {
        var branch = await GetGitBranchAsync(dir);
        var shortDir = ShortenPath(dir);
        var title = branch is not null ? $"{shortDir} ({branch})" : shortDir;
        TitleChanged?.Invoke(title);
    }

    private static string ShortenPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            return "~" + path[home.Length..].Replace('\\', '/');
        return Path.GetFileName(path.TrimEnd('/', '\\'));
    }

    private static async Task<string?> GetGitBranchAsync(string dir)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --abbrev-ref HEAD")
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }

    public void StartReading()
    {
        _ = ReadOutputLoopAsync(_cts.Token);
    }

    private async Task ReadOutputLoopAsync(CancellationToken ct)
    {
        var byteBuffer = new byte[4096];
        var charBuffer = new char[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead = await _pty.OutputStream.ReadAsync(byteBuffer, ct);
                if (bytesRead == 0) break;

                int charCount = _utf8Decoder.GetChars(
                    byteBuffer.AsSpan(0, bytesRead),
                    charBuffer.AsSpan(),
                    flush: false);

                if (charCount > 0)
                {
                    lock (SyncRoot)
                    {
                        _parser.Process(charBuffer.AsSpan(0, charCount));
                    }
                }

                OutputReceived?.Invoke();
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    public async Task SendInputAsync(byte[] data)
    {
        // Auto-scroll to bottom when user types
        lock (SyncRoot)
        {
            _grid.ViewportOffset = 0;
        }

        try
        {
            await _pty.InputStream.WriteAsync(data);
            await _pty.InputStream.FlushAsync();
        }
        catch (IOException) { }
    }

    /// <summary>
    /// Resizes the grid and PTY.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        lock (SyncRoot)
        {
            _grid.Resize(columns, rows);
        }
        _pty.Resize(columns, rows);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _pty.DisposeAsync();
        _cts.Dispose();
    }
}
