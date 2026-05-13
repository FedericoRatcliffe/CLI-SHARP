using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CliSharp.Application.Terminal;
using CliSharp.Infrastructure.ConPty;
using CliSharp.UI.Config;
using CliSharp.UI.AI;
using CliSharp.UI.Controls;
using CliSharp.UI.Layout;
using TerminalFontMetrics = CliSharp.UI.Rendering.FontMetrics;

namespace CliSharp.UI.Views;

public partial class MainWindow : Window
{
    private readonly ConfigManager _configManager = new();
    private readonly CommandHistory _history = new();
    private readonly AiService _ai = new();
    private readonly List<TabInfo> _tabs = [];
    private int _activeTab = -1;
    private bool _overlayOpen;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
    }

    // ── Lifecycle ───────────────────────────────────────────────────

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var theme = _configManager.Config.Theme;
        Background = new SolidColorBrush(Color.Parse(theme.Background));
        TerminalHost.Background = Background;

        _ai.Configure(_configManager.Config.Ai.ApiKey, _configManager.Config.Ai.Model);

        _configManager.ConfigChanged += config =>
            Dispatcher.UIThread.Post(() => ApplyConfig(config));

        CreateNewTab();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        foreach (var tab in _tabs) await tab.DisposeAsync();
        _configManager.Dispose();
    }

    private void ApplyConfig(AppConfig config)
    {
        _ai.Configure(config.Ai.ApiKey, config.Ai.Model);
        Background = new SolidColorBrush(Color.Parse(config.Theme.Background));
        TerminalHost.Background = Background;
        var font = new TerminalFontMetrics(config.Font.Family, config.Font.Size);
        foreach (var tab in _tabs)
            foreach (var pane in tab.AllPanes)
            {
                pane.Canvas.Renderer.ApplyTheme(config.Theme);
                pane.Canvas.Renderer.UpdateFont(font);
                pane.Canvas.InvalidateVisual();
            }
    }

    // ── Pane factory ────────────────────────────────────────────────

    private TerminalPane CreatePane(int cols, int rows)
    {
        var config = _configManager.Config;
        var font = new TerminalFontMetrics(config.Font.Family, config.Font.Size);
        var canvas = new TerminalCanvas();
        canvas.Renderer.ApplyTheme(config.Theme);
        canvas.Renderer.UpdateFont(font);

        var shell = DetectShell(config.Shell);
        var pty = ConPtyConnection.Start(shell, cols, rows);
        var session = new TerminalSession(pty, cols, rows);
        var pane = new TerminalPane { Session = session, Canvas = canvas };

        canvas.SetGrid(session.Grid, session.SyncRoot);

        // Wrap SendInput to track history
        canvas.SendInput = async data =>
        {
            _history.TrackInput(data);
            await session.SendInputAsync(data);
        };

        session.OutputReceived += () =>
            Dispatcher.UIThread.Post(() => canvas.InvalidateVisual());
        session.TitleChanged += title =>
            Dispatcher.UIThread.Post(() => { pane.Title = title; UpdateTabBar(); });
        session.ProcessExited += () =>
            Dispatcher.UIThread.Post(() => { pane.Title += " [exit]"; UpdateTabBar(); });
        session.ClipboardSetRequested += text =>
            Dispatcher.UIThread.Post(async () =>
            {
                var clip = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clip is not null) await clip.SetTextAsync(text);
            });

        session.BellRung += () =>
            Dispatcher.UIThread.Post(() => canvas.FlashBell());

        session.StartReading();
        return pane;
    }

    private (int cols, int rows) GetPaneSize()
    {
        var font = new TerminalFontMetrics(
            _configManager.Config.Font.Family, _configManager.Config.Font.Size);
        double w = TerminalHost.Bounds.Width > 0 ? TerminalHost.Bounds.Width : ClientSize.Width;
        double h = TerminalHost.Bounds.Height > 0 ? TerminalHost.Bounds.Height : ClientSize.Height - 32;
        return (Math.Max(20, (int)(w / font.CellWidth)), Math.Max(5, (int)(h / font.CellHeight)));
    }

    // ── Tabs ────────────────────────────────────────────────────────

    private void CreateNewTab()
    {
        try
        {
            var (cols, rows) = GetPaneSize();
            var pane = CreatePane(cols, rows);
            _tabs.Add(new TabInfo { Root = pane, ActivePane = pane });
            SwitchToTab(_tabs.Count - 1);
        }
        catch (Exception ex) { Title = $"CliSharp — Error: {ex.Message}"; }
    }

    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _activeTab = index;
        RebuildLayout();
        _tabs[index].ActivePane?.Canvas.Focus();
        UpdateTabBar();
    }

    private async void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        var tab = _tabs[index];
        _tabs.RemoveAt(index);
        await tab.DisposeAsync();
        if (_tabs.Count == 0) { Close(); return; }
        SwitchToTab(Math.Min(index, _tabs.Count - 1));
    }

    private void NextTab() => SwitchToTab((_activeTab + 1) % _tabs.Count);
    private void PrevTab() => SwitchToTab((_activeTab - 1 + _tabs.Count) % _tabs.Count);

    // ── Splits ──────────────────────────────────────────────────────

    private void SplitActive(SplitDirection direction)
    {
        if (_activeTab < 0) return;
        var tab = _tabs[_activeTab];
        if (tab.ActivePane is null) return;
        try
        {
            var (cols, rows) = GetPaneSize();
            int nc = direction == SplitDirection.Horizontal ? cols / 2 : cols;
            int nr = direction == SplitDirection.Vertical ? rows / 2 : rows;
            var newPane = CreatePane(Math.Max(20, nc), Math.Max(5, nr));
            var branch = new SplitBranch
            {
                Direction = direction, Ratio = 0.5,
                First = tab.ActivePane, Second = newPane,
            };
            tab.Root = SplitTree.Replace(tab.Root, tab.ActivePane, branch);
            tab.ActivePane = newPane;
            RebuildLayout();
            newPane.Canvas.Focus();
        }
        catch (Exception ex) { Title = $"CliSharp — Error: {ex.Message}"; }
    }

    private async void CloseActivePane()
    {
        if (_activeTab < 0) return;
        var tab = _tabs[_activeTab];
        if (tab.ActivePane is null) return;
        if (tab.Root == tab.ActivePane) { CloseTab(_activeTab); return; }
        var dying = tab.ActivePane;
        var newRoot = SplitTree.Remove(tab.Root, dying);
        if (newRoot is null) { CloseTab(_activeTab); return; }
        tab.Root = newRoot;
        var panes = tab.AllPanes;
        tab.ActivePane = panes.Count > 0 ? panes[0] : null;
        await dying.DisposeAsync();
        RebuildLayout();
        tab.ActivePane?.Canvas.Focus();
    }

    private void FocusNextPane()
    {
        if (_activeTab < 0) return;
        var tab = _tabs[_activeTab];
        var panes = tab.AllPanes;
        if (panes.Count <= 1) return;
        int idx = panes.IndexOf(tab.ActivePane!);
        tab.ActivePane = panes[(idx + 1) % panes.Count];
        RebuildLayout();
        tab.ActivePane.Canvas.Focus();
    }

    private void FocusPrevPane()
    {
        if (_activeTab < 0) return;
        var tab = _tabs[_activeTab];
        var panes = tab.AllPanes;
        if (panes.Count <= 1) return;
        int idx = panes.IndexOf(tab.ActivePane!);
        tab.ActivePane = panes[(idx - 1 + panes.Count) % panes.Count];
        RebuildLayout();
        tab.ActivePane.Canvas.Focus();
    }

    // ── Layout builder ──────────────────────────────────────────────

    private void RebuildLayout()
    {
        if (_activeTab < 0 || _activeTab >= _tabs.Count) return;
        TerminalHost.Children.Clear();
        TerminalHost.Children.Add(BuildLayout(_tabs[_activeTab].Root, _tabs[_activeTab].ActivePane));
        Dispatcher.UIThread.Post(ResizeAllPanes);
    }

    private Control BuildLayout(SplitNode node, TerminalPane? activePane)
    {
        if (node is TerminalPane pane)
        {
            bool isActive = pane == activePane;
            return new Border
            {
                BorderBrush = new SolidColorBrush(isActive ? Color.Parse("#89B4FA") : Color.Parse("#313244")),
                BorderThickness = new Thickness(isActive ? 1 : 0.5),
                Child = pane.Canvas,
            };
        }

        if (node is SplitBranch branch)
        {
            var grid = new Avalonia.Controls.Grid();
            var first = BuildLayout(branch.First, activePane);
            var divider = new Border { Background = new SolidColorBrush(Color.Parse("#45475A")) };
            var second = BuildLayout(branch.Second, activePane);

            if (branch.Direction == SplitDirection.Horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(branch.Ratio, GridUnitType.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(3, GridUnitType.Pixel));
                grid.ColumnDefinitions.Add(new ColumnDefinition(1 - branch.Ratio, GridUnitType.Star));
                Avalonia.Controls.Grid.SetColumn(first, 0);
                Avalonia.Controls.Grid.SetColumn(divider, 1);
                Avalonia.Controls.Grid.SetColumn(second, 2);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition(branch.Ratio, GridUnitType.Star));
                grid.RowDefinitions.Add(new RowDefinition(3, GridUnitType.Pixel));
                grid.RowDefinitions.Add(new RowDefinition(1 - branch.Ratio, GridUnitType.Star));
                Avalonia.Controls.Grid.SetRow(first, 0);
                Avalonia.Controls.Grid.SetRow(divider, 1);
                Avalonia.Controls.Grid.SetRow(second, 2);
            }

            grid.Children.Add(first);
            grid.Children.Add(divider);
            grid.Children.Add(second);
            return grid;
        }
        return new Panel();
    }

    // ── Resize ──────────────────────────────────────────────────────

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_activeTab >= 0 && _activeTab < _tabs.Count)
            Dispatcher.UIThread.Post(ResizeAllPanes);
    }

    private void ResizeAllPanes()
    {
        if (_activeTab < 0 || _activeTab >= _tabs.Count) return;
        foreach (var pane in _tabs[_activeTab].AllPanes)
        {
            var font = pane.Canvas.Renderer.Font;
            var b = pane.Canvas.Bounds;
            if (b.Width <= 0 || b.Height <= 0) continue;
            int c = Math.Max(20, (int)(b.Width / font.CellWidth));
            int r = Math.Max(5, (int)(b.Height / font.CellHeight));
            if (c != pane.Session.Grid.Columns || r != pane.Session.Grid.Rows)
            {
                pane.Session.Resize(c, r);
                pane.Canvas.InvalidateMeasure();
                pane.Canvas.InvalidateVisual();
            }
        }
    }

    // ── Tab bar UI ──────────────────────────────────────────────────

    private void UpdateTabBar()
    {
        var theme = _configManager.Config.Theme;
        var activeBg = Color.Parse(theme.Background);
        var activeFg = Color.Parse(theme.Foreground);
        var inactiveFg = Color.Parse("#6C7086");
        TabStrip.Children.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            int idx = i;
            bool isActive = i == _activeTab;
            var tb = new TextBlock
            {
                Text = $" {_tabs[i].ActivePane?.Title ?? "Terminal"} ",
                Foreground = new SolidColorBrush(isActive ? activeFg : inactiveFg),
                Background = new SolidColorBrush(isActive ? activeBg : Colors.Transparent),
                FontSize = 12, Padding = new Thickness(10, 6), Margin = new Thickness(1, 2, 1, 0),
            };
            tb.PointerPressed += (_, _) => SwitchToTab(idx);
            TabStrip.Children.Add(tb);
        }
        var addBtn = new TextBlock
        {
            Text = " + ", Foreground = new SolidColorBrush(inactiveFg),
            FontSize = 13, Padding = new Thickness(8, 5), Margin = new Thickness(4, 2, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        addBtn.PointerPressed += (_, _) => CreateNewTab();
        TabStrip.Children.Add(addBtn);

        Title = _activeTab >= 0 && _activeTab < _tabs.Count
            ? $"CliSharp — {_tabs[_activeTab].ActivePane?.Title ?? "Terminal"}"
            : "CliSharp";
    }

    // ── Overlays (command palette + history search) ──────────────────

    private void ShowOverlay(PaletteOverlay overlay)
    {
        if (_overlayOpen) return;
        _overlayOpen = true;
        overlay.Dismissed += DismissOverlay;
        OverlayHost.Children.Clear();
        OverlayHost.Children.Add(overlay);
        OverlayHost.IsVisible = true;
        overlay.FocusSearch();
    }

    private void DismissOverlay()
    {
        _overlayOpen = false;
        OverlayHost.Children.Clear();
        OverlayHost.IsVisible = false;
        // Clear search highlights when closing
        if (_activeTab >= 0 && _activeTab < _tabs.Count)
            _tabs[_activeTab].ActivePane?.Canvas.SetSearch(null);
        _tabs[_activeTab].ActivePane?.Canvas.Focus();
    }

    // ── Search overlay (Ctrl+Shift+F) ────────────────────────────────

    private void ShowSearchOverlay()
    {
        if (_overlayOpen || _activeTab < 0) return;
        _overlayOpen = true;

        var canvas = _tabs[_activeTab].ActivePane?.Canvas;
        if (canvas is null) return;

        var searchBox = new Avalonia.Controls.TextBox
        {
            Watermark = "Search...",
            FontSize = 14,
            Padding = new Thickness(10, 8),
        };

        var statusText = new Avalonia.Controls.TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#6C7086")),
            FontSize = 12,
            Margin = new Thickness(8, 4, 0, 0),
        };

        var card = new Avalonia.Controls.Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#89B4FA")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Width = 400,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 16, 0),
            Child = new Avalonia.Controls.StackPanel
            {
                Spacing = 4,
                Children = { searchBox, statusText },
            },
        };

        var overlay = new Avalonia.Controls.Panel
        {
            Children = { card },
        };

        void UpdateSearch()
        {
            canvas.SetSearch(searchBox.Text);
            int count = canvas.SearchMatchCount;
            int idx = canvas.ActiveMatchIndex;
            statusText.Text = count > 0 ? $"{idx + 1} / {count} matches" : searchBox.Text?.Length > 0 ? "No matches" : "";
            canvas.InvalidateVisual();
        }

        void CloseSearch()
        {
            _overlayOpen = false;
            OverlayHost.Children.Clear();
            OverlayHost.IsVisible = false;
            canvas.SetSearch(null);
            canvas.Focus();
        }

        searchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.TextBox.TextProperty)
                UpdateSearch();
        };

        searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseSearch(); e.Handled = true; }
            else if (e.Key == Key.Enter || e.Key == Key.F3)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) canvas.PrevMatch();
                else canvas.NextMatch();
                UpdateSearch();
                e.Handled = true;
            }
        };

        OverlayHost.Children.Clear();
        OverlayHost.Children.Add(overlay);
        OverlayHost.IsVisible = true;
        searchBox.Focus();
    }

    private void ShowCommandPalette()
    {
        var items = new List<PaletteOverlay.Item>
        {
            new("New Tab", "Ctrl+Shift+T", CreateNewTab),
            new("Close Pane", "Ctrl+Shift+W", CloseActivePane),
            new("Split Horizontal", "Ctrl+Shift+D", () => SplitActive(SplitDirection.Horizontal)),
            new("Split Vertical", "Ctrl+Shift+E", () => SplitActive(SplitDirection.Vertical)),
            new("Next Tab", "Ctrl+Tab", NextTab),
            new("Previous Tab", "Ctrl+Shift+Tab", PrevTab),
            new("Search in Terminal", "Ctrl+Shift+F", ShowSearchOverlay),
            new("Copy Selection", "Ctrl+Shift+C", () =>
            {
                if (_activeTab >= 0 && _tabs[_activeTab].ActivePane?.Canvas.HasSelection == true)
                    _ = _tabs[_activeTab].ActivePane!.Canvas.CopySelectionAsync();
            }),
            new("Focus Next Pane", "Alt+→", FocusNextPane),
            new("Focus Previous Pane", "Alt+←", FocusPrevPane),
            new("Theme: Catppuccin Mocha", null, () => ApplyThemeByName(ThemeConfig.CatppuccinMocha())),
            new("Theme: One Dark", null, () => ApplyThemeByName(ThemeConfig.OneDark())),
            new("Theme: Dracula", null, () => ApplyThemeByName(ThemeConfig.Dracula())),
            new("Theme: Catppuccin Latte (Light)", null, () => ApplyThemeByName(ThemeConfig.CatppuccinLatte())),
            new("Theme: Buzz Lightyear", null, () => ApplyThemeByName(ThemeConfig.BuzzLightyear())),
            new("History Search", "Ctrl+R", ShowHistorySearch),
            new("\u2728 AI: Generate Command", "Ctrl+Shift+A", ShowAiGenerate),
            new("\u2728 AI: Explain Last Command", null, ShowAiExplain),
            new("\u2728 AI: Debug Last Error", null, ShowAiDebug),
            new("\u2728 AI: Summarize Output", null, ShowAiSummarize),
        };

        // Workflows from config
        foreach (var wf in _configManager.Config.Workflows)
        {
            var cmd = wf.Command;
            items.Add(new PaletteOverlay.Item($"\u25b6 {wf.Name}", null, () => SendCommandToActivePane(cmd)));
        }

        ShowOverlay(new PaletteOverlay(items, "Search commands..."));
    }

    private void ApplyThemeByName(ThemeConfig theme)
    {
        var config = _configManager.Config;
        config.Theme = theme;
        ApplyConfig(config);
    }

    private void ShowHistorySearch()
    {
        var entries = _history.Entries;
        // Show most recent first
        var items = new List<PaletteOverlay.Item>();
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var cmd = entries[i];
            items.Add(new PaletteOverlay.Item(cmd, null, () => SendCommandToActivePane(cmd)));
        }

        if (items.Count == 0)
            items.Add(new PaletteOverlay.Item("(no history yet)", null, null));

        ShowOverlay(new PaletteOverlay(items, "Search history..."));
    }

    private void SendCommandToActivePane(string command)
    {
        if (_activeTab < 0) return;
        var pane = _tabs[_activeTab].ActivePane;
        if (pane?.Canvas.SendInput is null) return;

        // Ctrl+U (clear line) + command text (sin Enter, para que el usuario lo revise)
        var clearLine = new byte[] { 0x15 };
        var cmdBytes = Encoding.UTF8.GetBytes(command);
        var combined = new byte[clearLine.Length + cmdBytes.Length];
        clearLine.CopyTo(combined, 0);
        cmdBytes.CopyTo(combined, clearLine.Length);
        _ = pane.Canvas.SendInput(combined);
    }

    // ── AI ───────────────────────────────────────────────────────────

    private void ShowAiOverlay(AiOverlay overlay)
    {
        if (_overlayOpen) return;
        _overlayOpen = true;
        overlay.Dismissed += DismissOverlay;
        overlay.ExecuteCommand += cmd => SendCommandToActivePane(cmd);
        overlay.CopyToClipboard += text =>
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip is not null) _ = clip.SetTextAsync(text);
        };
        OverlayHost.Children.Clear();
        OverlayHost.Children.Add(overlay);
        OverlayHost.IsVisible = true;
        overlay.FocusInput();
    }

    private void ShowAiGenerate()
    {
        ShowAiOverlay(new AiOverlay(_ai, AiPrompts.GenerateCommand,
            "Describe what you want to do..."));
    }

    private void ShowAiExplain()
    {
        var lastCmd = _history.Entries.Count > 0 ? _history.Entries[^1] : null;
        if (lastCmd is null) return;
        ShowAiOverlay(new AiOverlay(_ai, AiPrompts.ExplainCommand,
            "Explaining command...", prefill: lastCmd));
    }

    private void ShowAiDebug()
    {
        var lastCmd = _history.Entries.Count > 0 ? _history.Entries[^1] : null;
        if (lastCmd is null) return;
        // Read last output lines from active grid as context
        var grid = _tabs[_activeTab].ActivePane?.Session.Grid;
        var context = lastCmd + "\n\n" + GetRecentOutput(grid, 20);
        ShowAiOverlay(new AiOverlay(_ai, AiPrompts.DebugError,
            "Debugging...", prefill: $"Command: {lastCmd}", prefillContext: context));
    }

    private void ShowAiSummarize()
    {
        var grid = _tabs[_activeTab].ActivePane?.Session.Grid;
        var output = GetRecentOutput(grid, 50);
        if (string.IsNullOrWhiteSpace(output)) return;
        ShowAiOverlay(new AiOverlay(_ai, AiPrompts.SummarizeOutput,
            "Summarizing...", prefill: "Summarize this output", prefillContext: output));
    }

    private static string GetRecentOutput(Application.Terminal.Grid? grid, int maxRows)
    {
        if (grid is null) return "";
        var sb = new StringBuilder();
        int start = Math.Max(0, grid.Rows - maxRows);
        for (int r = start; r < grid.Rows; r++)
        {
            var cells = grid.GetRow(r);
            var line = new char[grid.Columns];
            for (int c = 0; c < grid.Columns; c++) line[c] = cells[c].Character;
            var trimmed = new string(line).TrimEnd();
            if (trimmed.Length > 0) sb.AppendLine(trimmed);
        }
        return sb.ToString();
    }

    // ── Keyboard shortcuts (tunnel) ─────────────────────────────────

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (_overlayOpen) return;

        var cs = KeyModifiers.Control | KeyModifiers.Shift;

        if (e.KeyModifiers == cs) switch (e.Key)
        {
            case Key.T: CreateNewTab(); e.Handled = true; return;
            case Key.W: CloseActivePane(); e.Handled = true; return;
            case Key.D: SplitActive(SplitDirection.Horizontal); e.Handled = true; return;
            case Key.E: SplitActive(SplitDirection.Vertical); e.Handled = true; return;
            case Key.A: ShowAiGenerate(); e.Handled = true; return;
            case Key.P: ShowCommandPalette(); e.Handled = true; return;
            case Key.F: ShowSearchOverlay(); e.Handled = true; return;
            case Key.C: // Copy selection
                if (_activeTab >= 0 && _tabs[_activeTab].ActivePane?.Canvas.HasSelection == true)
                { _ = _tabs[_activeTab].ActivePane!.Canvas.CopySelectionAsync(); e.Handled = true; return; }
                break;
        }

        if (e.KeyModifiers == KeyModifiers.Control) switch (e.Key)
        {
            case Key.Tab: NextTab(); e.Handled = true; return;
            case Key.R: ShowHistorySearch(); e.Handled = true; return;
        }

        if (e.KeyModifiers == cs && e.Key == Key.Tab)
        { PrevTab(); e.Handled = true; return; }

        if (e.KeyModifiers == KeyModifiers.Alt) switch (e.Key)
        {
            case Key.Right: case Key.Down: FocusNextPane(); e.Handled = true; return;
            case Key.Left: case Key.Up: FocusPrevPane(); e.Handled = true; return;
        }
    }

    // ── Shell auto-detection ────────────────────────────────────────

    private static string DetectShell(string configured)
    {
        // Only auto-detect when using the default
        if (!string.Equals(configured, "powershell.exe", StringComparison.OrdinalIgnoreCase))
            return configured;

        // Prefer pwsh.exe (PowerShell 7+) if available
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pwsh.exe", "--version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            });
            proc?.WaitForExit(2000);
            if (proc is { ExitCode: 0 }) return "pwsh.exe";
        }
        catch { }

        return configured;
    }

    // ── Tab model ───────────────────────────────────────────────────

    private sealed class TabInfo : IAsyncDisposable
    {
        public required SplitNode Root { get; set; }
        public TerminalPane? ActivePane { get; set; }
        public List<TerminalPane> AllPanes => SplitTree.CollectPanes(Root);
        public async ValueTask DisposeAsync()
        {
            foreach (var pane in AllPanes) await pane.DisposeAsync();
        }
    }
}
