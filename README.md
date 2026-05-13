# CLI-SHARP

A modern, GPU-accelerated terminal emulator built with **C# / .NET 8** and **Avalonia UI**, inspired by [Warp](https://www.warp.dev/), [Alacritty](https://github.com/alacritty/alacritty), and [Windows Terminal](https://github.com/microsoft/terminal).

Designed as a daily-driver terminal for developers, with features like command blocks, AI assistance, split panes, text selection, scrollback search, and a fully customizable theme system.

---

## Features

### Terminal Emulator
- **Full ANSI/VT parser** — Paul Williams state machine (14 states, table-driven)
- **Color support** — 16 standard, 256 indexed, and 24-bit truecolor (RGB)
- **Unicode** — Double-width CJK characters, fullwidth forms
- **ConPTY** — Native Windows pseudo-terminal via P/Invoke (Windows 10 1809+)
- **Alt screen buffer** — `?1049` for vim, less, htop
- **Application cursor keys** — `?1` for vim arrow keys
- **Cursor shapes** — Block, underline, and bar (I-beam) via CSI SP q (DECSCUSR)
- **Bracketed paste** — `?2004` prevents accidental command execution
- **Mouse reporting** — `?1000`, `?1002`, `?1003` + SGR mode `?1006`
- **OSC sequences** — Title (`0/2`), CWD (`7`), hyperlinks (`8`), clipboard (`52`), command blocks (`133`), inline images (`1337`)
- **Scrollback** — 1000 lines, smooth 1-line scroll steps
- **Visual bell** — Brief screen flash on BEL character
- **Shell auto-detection** — Prefers `pwsh.exe` (PowerShell 7+) when available

### Modern UX
- **Tabs** — Multiple sessions, close button per tab, hover highlight
- **Split panes** — Horizontal and vertical splits (binary tree layout), active pane indicator
- **Text selection** — Mouse drag to select, blue highlight overlay
- **Scrollback search** — Live search with match counter, yellow highlights, F3 navigation
- **Command blocks** — OSC 133 shell integration with visual separators and exit code badges (&#x2713;/&#x2717;)
- **Command palette** — Fuzzy search over all actions, themes, and workflows
- **History search** — Fuzzy matching with consecutive/word-start bonuses
- **Themes** — 5 built-in themes, hot-reloadable via JSON config
- **Status bar** — Shows shell name, grid size (cols x rows), font size, current directory
- **Font zoom** — Ctrl+/- to resize font live, Ctrl+0 to reset
- **Underline & strikethrough** — Rendered as decorations on text runs

### Developer Productivity
- **Link detection** — Ctrl+Click opens URLs in the browser and file paths (`src/foo.cs:42:10`) in VS Code
- **Shell integration** — OSC 7 for CWD tracking, automatic git branch detection in tab titles
- **Workflows** — Parameterized command snippets defined in config, accessible from the palette

### AI Integration
- **Generate command** — Describe what you want in natural language, AI returns the shell command with an Execute button
- **Explain command** — AI explains the last command in plain language
- **Debug errors** — AI analyzes failed commands with exit code and output context
- **Summarize output** — AI summarizes the last N lines of terminal output
- Powered by **Claude API** (Anthropic) — requires API key in config

### Performance
- **Glyph cache** — `FormattedText` objects cached by `(char, bold, color)` tuple
- **StringBuilder reuse** — Single instance reused across all text runs per frame
- **Background rect batching** — Consecutive same-color cells merged into single draw calls
- **Render throttle** — Capped at ~60fps to prevent UI thread saturation during heavy output
- **Text run batching** — Consecutive same-style characters drawn in one call, enabling **font ligatures** (Fira Code, JetBrains Mono, Cascadia Code)
- **Inline images** — iTerm2 protocol (OSC 1337) with bitmap caching
- **GPU-accelerated** — Avalonia renders via Skia (Direct3D on Windows)

---

## Requirements

- **Windows 10 version 1809** or later (for ConPTY support)
- **.NET 8 SDK** or later — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Git** (optional, for git branch display in tabs)
- **VS Code** (optional, for Ctrl+Click file path opening)

---

## Getting Started

### Clone and run

```bash
git clone https://github.com/YOUR_USERNAME/cli-sharp.git
cd cli-sharp/Back
dotnet run --project src/SharpTerminal.UI
```

### Build release

```bash
dotnet build SharpTerminal.sln --configuration Release
```

### Run tests

```bash
dotnet test SharpTerminal.sln
```

There are **102 unit tests** covering the ANSI parser, grid operations, and GridManager integration.

### Publish as single-file executable

```bash
dotnet publish src/SharpTerminal.UI/SharpTerminal.UI.csproj --configuration Release --runtime win-x64 --output ./publish
```

This produces a **self-contained** `SharpTerminal.UI.exe` (~88 MB) that runs on any Windows 10+ machine **without requiring .NET SDK installed**. Just copy and run.

---

## Keyboard Shortcuts

### Tabs & Panes

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+T` | New tab |
| `Ctrl+Shift+W` | Close current pane (closes tab if last pane) |
| `Ctrl+Tab` | Next tab |
| `Ctrl+Shift+Tab` | Previous tab |
| `Ctrl+Shift+D` | Split horizontally (side by side) |
| `Ctrl+Shift+E` | Split vertically (top/bottom) |
| `Alt+Arrow` | Move focus between panes |
| Tab close button (`x`) | Close specific tab |

### Search & Selection

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+F` | Open scrollback search |
| `Enter` / `F3` | Next search match |
| `Shift+F3` | Previous search match |
| `Escape` | Close search |
| `Ctrl+C` (with selection) | Copy selected text to clipboard |
| `Ctrl+Shift+C` | Copy selected text to clipboard |
| Mouse drag | Select text |
| Click | Clear selection |

### Tools

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+P` | Command palette |
| `Ctrl+R` | Fuzzy history search |
| `Ctrl+Shift+A` | AI: Generate command |
| `Ctrl+V` | Paste (with bracketed paste mode support) |
| Right-click | Paste from clipboard |
| Middle-click | Paste from clipboard |
| `Ctrl+Click` | Open URL in browser / file path in VS Code |

### Font

| Shortcut | Action |
|----------|--------|
| `Ctrl+=` / `Ctrl++` | Increase font size (+2pt) |
| `Ctrl+-` | Decrease font size (-2pt) |
| `Ctrl+0` | Reset font to config default |

### Terminal

| Shortcut | Action |
|----------|--------|
| `Ctrl+A..Z` | Send control character (Ctrl+C = interrupt when no selection) |
| Arrow keys | Cursor movement (application mode aware) |
| Mouse wheel | Scroll through scrollback (1 line per step) |

---

## Configuration

CLI-SHARP stores its configuration at:

```
~/.clisharp/config.json
```

A default config is created on first run. All changes are applied instantly via hot-reload.

### Example config

```json
{
  "font": {
    "family": "Cascadia Code,Consolas,monospace",
    "size": 14
  },
  "shell": "powershell.exe",
  "scrollback": 1000,
  "theme": {
    "name": "Catppuccin Mocha",
    "foreground": "#CDD6F4",
    "background": "#1E1E2E",
    "cursor": "#F5E0DC",
    "palette": [
      "#45475A", "#F38BA8", "#A6E3A1", "#F9E2AF",
      "#89B4FA", "#CBA6F7", "#94E2D5", "#BAC2DE",
      "#585B70", "#F38BA8", "#A6E3A1", "#F9E2AF",
      "#89B4FA", "#CBA6F7", "#94E2D5", "#CDD6F4"
    ]
  },
  "ai": {
    "apiKey": "",
    "model": "claude-sonnet-4-20250514"
  },
  "workflows": [
    { "name": "Docker Run", "command": "docker run -p {port}:80 {image}" },
    { "name": "Git Commit All", "command": "git add -A && git commit -m \"{message}\"" },
    { "name": "NPM Dev", "command": "npm run dev" },
    { "name": "Dotnet Watch", "command": "dotnet watch run --project {project}" }
  ]
}
```

### Shell options

Change `"shell"` to use a different shell. When set to `powershell.exe`, CLI-SHARP automatically detects and prefers `pwsh.exe` (PowerShell 7+) if installed.

```json
"shell": "powershell.exe"
"shell": "pwsh.exe"
"shell": "cmd.exe"
"shell": "wsl.exe"
"shell": "C:\\Program Files\\Git\\bin\\bash.exe"
```

### AI setup

Set your [Anthropic API key](https://console.anthropic.com/) to enable AI features:

```json
"ai": {
  "apiKey": "sk-ant-api03-...",
  "model": "claude-sonnet-4-20250514"
}
```

Without an API key, AI features show a configuration message instead of results.

---

## Built-in Themes

| Theme | Background | Style |
|-------|-----------|-------|
| **Catppuccin Mocha** | `#1E1E2E` | Dark, warm pastels (default) |
| **One Dark** | `#282C34` | Dark, Atom-inspired |
| **Dracula** | `#282A36` | Dark, high contrast |
| **Catppuccin Latte** | `#EFF1F5` | Light, warm pastels |
| **Buzz Lightyear** | `#5F396D` | Purple, green, red accents |

Switch themes instantly via `Ctrl+Shift+P` → type the theme name.

You can also define custom themes directly in `config.json` by editing the `theme` object.

---

## Architecture

Clean Architecture with 4 layers:

```
┌─────────────────────────────────────────────────────────┐
│  UI (Avalonia)                                          │
│  MainWindow, TerminalCanvas, TerminalRenderer,          │
│  PaletteOverlay, AiOverlay, AiService, ConfigManager    │
├─────────────────────────────────────────────────────────┤
│  Infrastructure                                         │
│  ConPtyConnection, PseudoConsole, NativeMethods          │
├─────────────────────────────────────────────────────────┤
│  Application                                            │
│  AnsiParser, TransitionTable, Grid, GridManager,        │
│  TerminalSession, CommandHistory, LinkDetector           │
├─────────────────────────────────────────────────────────┤
│  Domain                                                 │
│  Cell, TerminalColor, CellAttributes, CellFlags          │
└─────────────────────────────────────────────────────────┘
```

### Key design decisions

- **ANSI Parser**: Table-driven state machine (Paul Williams model), 14 states x 128 bytes transition table, processes `ReadOnlySpan<char>` after UTF-8 decoding
- **Grid**: Jagged `Cell[][]` array for O(1) scroll (reference swap), parallel `byte[]` for command block markers, `InlineImageData` list for iTerm2 images
- **Rendering**: Avalonia `DrawingContext` with glyph cache, StringBuilder reuse, background rect batching, and text run batching for ligature support. Render throttled to ~60fps.
- **Threading**: Background reader takes `lock(SyncRoot)` to update grid; UI thread takes the same lock to render. Coarse-grained but correct.
- **Splits**: Binary tree (`SplitBranch` / `TerminalPane`) converted to nested `Avalonia.Controls.Grid` layouts
- **ConPTY**: Direct P/Invoke to `kernel32.dll` — `CreatePseudoConsole`, `CreateProcessW`, pipe I/O
- **Cursor**: Three shapes (block, underline, bar) via DECSCUSR, with 530ms blink timer that resets on input

---

## Project Structure

```
Back/
├── SharpTerminal.sln
├── nuget.config
├── .gitignore
├── README.md
├── LICENSE
│
├── src/
│   ├── SharpTerminal.Domain/
│   │   ├── Entities/          Cell.cs
│   │   ├── Enums/             AnsiColor.cs
│   │   └── ValueObjects/      TerminalColor.cs, CellAttributes.cs
│   │
│   ├── SharpTerminal.Application/
│   │   ├── Abstractions/      IPtyConnection.cs
│   │   ├── Parser/            AnsiParser.cs, TransitionTable.cs, IParserHandler.cs,
│   │   │                      ParserState.cs, ParserAction.cs
│   │   └── Terminal/          Grid.cs, GridManager.cs, TerminalSession.cs,
│   │                          CommandHistory.cs, LinkDetector.cs, UnicodeWidth.cs
│   │
│   ├── SharpTerminal.Infrastructure/
│   │   └── ConPty/            NativeMethods.cs, PseudoConsole.cs, ConPtyConnection.cs
│   │
│   └── SharpTerminal.UI/
│       ├── AI/                AiService.cs
│       ├── Config/            AppConfig.cs, ConfigManager.cs
│       ├── Controls/          TerminalCanvas.cs, PaletteOverlay.cs, AiOverlay.cs
│       ├── Layout/            SplitNode.cs
│       ├── Rendering/         TerminalRenderer.cs, FontMetrics.cs
│       ├── Views/             MainWindow.axaml, MainWindow.axaml.cs
│       ├── App.axaml, App.axaml.cs, Program.cs
│       └── SharpTerminal.UI.csproj
│
└── tests/
    ├── SharpTerminal.Tests.Unit/       102 tests (parser, grid, grid manager)
    └── SharpTerminal.Tests.Integration/
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia | 11.2.1 | Cross-platform UI framework |
| Avalonia.Desktop | 11.2.1 | Desktop runtime |
| Avalonia.Themes.Fluent | 11.2.1 | Fluent dark theme |
| Avalonia.Diagnostics | 11.2.1 | Dev tools (debug only) |
| xUnit | 2.x | Test framework |
| FluentAssertions | 6.x | Test assertions |

**Zero external dependencies** for the core terminal emulator. ConPTY, ANSI parser, grid, and renderer are all custom implementations using only .NET BCL. AI features use `HttpClient` directly (no SDK NuGet).

---

## Supported Escape Sequences

### CSI sequences (ESC [ ...)

| Sequence | Name | Description |
|----------|------|-------------|
| `CSI n A` | CUU | Cursor up |
| `CSI n B` | CUD | Cursor down |
| `CSI n C` | CUF | Cursor forward |
| `CSI n D` | CUB | Cursor backward |
| `CSI n E` | CNL | Cursor next line |
| `CSI n F` | CPL | Cursor previous line |
| `CSI n G` | CHA | Cursor character absolute |
| `CSI n;m H` | CUP | Cursor position |
| `CSI n J` | ED | Erase in display |
| `CSI n K` | EL | Erase in line |
| `CSI n d` | VPA | Vertical position absolute |
| `CSI ... m` | SGR | Select graphic rendition (colors, bold, etc.) |
| `CSI n SP q` | DECSCUSR | Set cursor shape (0-2 block, 3-4 underline, 5-6 bar) |

### SGR parameters (CSI ... m)

| Parameter | Effect |
|-----------|--------|
| `0` | Reset all attributes |
| `1` / `22` | Bold on / off |
| `3` / `23` | Italic on / off |
| `4` / `24` | Underline on / off |
| `7` / `27` | Inverse on / off |
| `8` / `28` | Hidden on / off |
| `9` / `29` | Strikethrough on / off |
| `30-37` | Foreground color (standard) |
| `38;5;n` | Foreground color (256) |
| `38;2;r;g;b` | Foreground color (RGB truecolor) |
| `39` | Default foreground |
| `40-47` | Background color (standard) |
| `48;5;n` | Background color (256) |
| `48;2;r;g;b` | Background color (RGB truecolor) |
| `49` | Default background |
| `90-97` | Bright foreground |
| `100-107` | Bright background |

### DECSET / DECRST (CSI ? n h/l)

| Mode | Name | Description |
|------|------|-------------|
| `?1` | DECCKM | Application cursor keys |
| `?25` | DECTCEM | Cursor visibility |
| `?47` / `?1047` / `?1049` | Alt screen | Alternate screen buffer |
| `?1000` | Mouse normal | Button press/release tracking |
| `?1002` | Mouse button | Button-event tracking |
| `?1003` | Mouse any | Any-event tracking |
| `?1006` | SGR mouse | Extended mouse coordinates |
| `?2004` | Bracketed paste | Wrap pasted text in markers |

### OSC sequences (ESC ] n ; data ST)

| OSC | Description |
|-----|-------------|
| `0` / `2` | Set window title |
| `7` | Set current working directory |
| `8` | Hyperlinks (clickable with Ctrl+Click) |
| `52` | Clipboard access (write only, read disabled for security) |
| `133` | Command blocks (FinalTerm shell integration) |
| `1337` | Inline images (iTerm2 protocol) |

### ESC sequences

| Sequence | Description |
|----------|-------------|
| `ESC M` | Reverse index |

---

## Contributing

Contributions are welcome! Here are some areas where help is appreciated:

- **Sixel / Kitty graphics protocol** — Alternative image protocols
- **Autocomplete engine** — Contextual completions (Fig-style specs)
- **SSH integration** — Detect SSH sessions, sync config
- **Cross-platform** — macOS/Linux PTY support (Avalonia already supports these platforms)
- **Tab drag reorder** — Reorder tabs via drag and drop
- **Performance profiling** — Benchmark with `vttest`, optimize hot paths

### Development

```bash
# Clone
git clone https://github.com/YOUR_USERNAME/cli-sharp.git
cd cli-sharp/Back

# Build
dotnet build SharpTerminal.sln

# Test
dotnet test SharpTerminal.sln

# Run
dotnet run --project src/SharpTerminal.UI

# Run in Release mode
dotnet run --project src/SharpTerminal.UI --configuration Release

# Publish standalone executable
dotnet publish src/SharpTerminal.UI/SharpTerminal.UI.csproj -c Release -r win-x64 -o ./publish
```

---

## License

MIT License. See [LICENSE](LICENSE) for details.

---

## Acknowledgments

Conceptual references (no code was copied):

- [Windows Terminal](https://github.com/microsoft/terminal) — ConPTY, VT engine architecture
- [Alacritty](https://github.com/alacritty/alacritty) — Clean parser/grid/renderer separation
- [vte](https://github.com/alacritty/vte) — Paul Williams ANSI state machine reference
- [WezTerm](https://github.com/wezterm/wezterm) — Multiplexing, configuration ideas
- [Ghostty](https://github.com/ghostty-org/ghostty) — Design decision documentation
- [Catppuccin](https://github.com/catppuccin/catppuccin) — Color palette (Mocha and Latte themes)
