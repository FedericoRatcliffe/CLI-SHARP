# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

SharpTerminal (folder still named CLI-SHARP) is a Windows terminal emulator written in C# / .NET 8 on Avalonia 11.2. It implements its own ANSI parser, grid, and renderer on top of native Windows ConPTY (P/Invoke into `kernel32.dll`). No external terminal libraries are used — only Avalonia and the .NET BCL.

## Commands

```powershell
# Build everything
dotnet build SharpTerminal.sln

# Run the app (UI is the entry point)
dotnet run --project src/SharpTerminal.UI

# Run all tests (unit + integration)
dotnet test SharpTerminal.sln

# Run a single test class or test
dotnet test tests/SharpTerminal.Tests.Unit --filter "FullyQualifiedName~AnsiParserTests"
dotnet test tests/SharpTerminal.Tests.Unit --filter "FullyQualifiedName=SharpTerminal.Tests.Unit.AnsiParserTests.SomeTestName"

# Publish a self-contained single-file exe (~88 MB)
dotnet publish src/SharpTerminal.UI/SharpTerminal.UI.csproj -c Release -r win-x64 -o ./publish
```

Note: the solution and csproj names still use the `SharpTerminal.*` prefix even though the repo folder is `CLI-SHARP`. Use the solution-relative paths shown above.

## Architecture

Clean Architecture in 4 projects. Dependency direction is strictly top-down: UI → Infrastructure + Application → Domain.

- **SharpTerminal.Domain** — `Cell`, `TerminalColor`, `CellAttributes`, `CellFlags`, `AnsiColor`. No dependencies, no Avalonia.
- **SharpTerminal.Application** — Parser + grid + session orchestrator. UI-agnostic. Depends only on Domain.
  - `Parser/AnsiParser.cs` — Paul Williams state machine. 14 states × 128-byte transition table in `TransitionTable.cs`. Dispatches via `IParserHandler` (the **only** seam between parser and grid).
  - `Terminal/Grid.cs` — Jagged `Cell[][]` so scrolling is an O(1) row-reference swap. Holds scrollback, alt screen, parallel `byte[]` for OSC 133 command-block markers, and an `InlineImageData` list for iTerm2 OSC 1337 images. Raises `TitleChanged`, `ClipboardSetRequested`, `BellRung`, `DirectoryChanged`.
  - `Terminal/GridManager.cs` — Implements `IParserHandler`. Translates parser callbacks (CSI/SGR/OSC/DECSET) into grid mutations.
  - `Terminal/TerminalSession.cs` — Orchestrator. Owns the background read loop, the UTF-8 `Decoder` (handles characters split across PTY reads), the parser, the grid manager, and the `SyncRoot` lock.
- **SharpTerminal.Infrastructure** — `ConPty/` only. Direct P/Invoke (`CreatePseudoConsole`, `CreateProcessW`, `ResizePseudoConsole`, pipe I/O). Implements `IPtyConnection` from Application.
- **SharpTerminal.UI** — Avalonia. `Controls/TerminalCanvas.cs` (input + selection + search overlay), `Rendering/TerminalRenderer.cs` (custom `DrawingContext` rendering with glyph cache + run batching), `Layout/SplitNode.cs` (binary tree → nested `Avalonia.Controls.Grid`), `AI/AiService.cs` (raw `HttpClient` to Anthropic, no SDK), `Config/ConfigManager.cs` (hot-reloads `~/.clisharp/config.json`).

### Threading model (important)

There is **one lock per session**: `TerminalSession.SyncRoot`.

- The background PTY reader takes `lock(SyncRoot)` to decode bytes and mutate the grid via the parser.
- The UI thread takes the **same** `lock(SyncRoot)` while rendering to read a consistent grid snapshot.
- `OutputReceived`, `ProcessExited`, `TitleChanged`, `ClipboardSetRequested`, `BellRung` all fire from the background thread. Any consumer in UI must `Dispatcher.UIThread.Post` before touching Avalonia objects.

This is coarse-grained on purpose. Do not introduce finer-grained locks on grid sub-structures — they will deadlock against this one. Do not call into Avalonia from inside the lock.

### Data flow

```
ConPty stdout pipe
  → byte[] buffer
  → UTF-8 Decoder (stateful, survives split codepoints across reads)
  → char[] / ReadOnlySpan<char>
  → AnsiParser (state machine, dispatches actions)
  → IParserHandler (GridManager)
  → Grid mutations
  → (renderer reads grid under same lock on next frame)
```

The parser does **not** decode UTF-8 — the session does, because UTF-8 sequences can be split across PTY reads and the `Decoder` instance must persist across calls.

### Render path

`TerminalRenderer` (UI-thread, custom `Render`):
- Glyph cache keyed by `(char, bold, fgColor)` returning `FormattedText`.
- Single reused `StringBuilder` per frame for text-run assembly.
- Background fill: consecutive cells with the same bg color batched into one `DrawRectangle`.
- Foreground: consecutive cells with the same style batched into one `DrawText` so font ligatures (Fira Code, JetBrains Mono, Cascadia Code) actually shape.
- Throttled to ~60fps — heavy PTY output won't saturate the UI thread.

If you change `Cell` layout, `CellAttributes`, or `CellFlags`, you almost certainly need to update both the glyph cache key in `TerminalRenderer` and the batching predicates.

## Conventions and constraints worth knowing

- **Windows-only.** ConPTY (`Windows 10 1809+`) is the only PTY backend. There is no abstraction layer yet for macOS/Linux PTYs even though Avalonia would support those platforms. Don't assume cross-platform.
- **No external terminal/ANSI library.** Custom parser, custom grid, custom renderer. When adding escape sequences, extend `AnsiParser` + `IParserHandler` + `GridManager` together — don't bypass the parser.
- **Shell auto-detection.** When config `shell` is `powershell.exe`, the app prefers `pwsh.exe` (PowerShell 7+) if found on PATH. See `ConPtyConnection`.
- **Config hot-reload.** `~/.clisharp/config.json` is watched and applied live. A default file is generated on first run.
- **OSC 52 read is disabled** for security — clipboard write only.
- **OSC 133** (FinalTerm) drives the command-block visuals and the success/failure badge — the parallel `byte[]` on `Grid` stores the per-row marker.
- **Ctrl+Click link-out** opens URLs in the default browser and `file:line:col` paths in VS Code (`code -g`). See `LinkDetector`.
- **AI calls go to the Anthropic API directly via `HttpClient`** (no Anthropic SDK). The model is configurable; an empty `apiKey` makes AI features show a config-required message instead of failing.

## Tests

`tests/SharpTerminal.Tests.Unit` covers the parser, grid, and grid-manager integration (~102 tests). When adding escape-sequence support, add a parser test that asserts the dispatched actions and a grid-manager test that asserts the resulting grid state — both layers are independently testable.

`tests/SharpTerminal.Tests.Integration` exists for end-to-end PTY + parser flows.
