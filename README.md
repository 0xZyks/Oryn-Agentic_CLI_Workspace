# Oryn — M0 engineering workspace

Oryn is a local-first engineering workspace with a **native SDL3 + SkiaSharp interface**. Its TUI-inspired frame uses monospace typography, compact bars, closely spaced panels, thin nearly square outlines, dark blue-black surfaces and a restrained lavender focus accent. Oryn assists engineering and architecture alongside existing code editors. **SDL + OpenGL remains the v1.0 rendering target.** Product vision: [README_ORYN.md](README_ORYN.md); implementation choice: [ADR 0006](docs/adr/0006-sdl3-skia-raster-m0.md).

M0 provides a lazy file explorer, read-only file previews, optional visualizer docking and resizable panels. Chat opens at startup. The central tabs are Chat, Sources (read-only previews) and Visualiser, in that order; a contextual inspector appears when the map has enough width. The focused panel is brighter and marked by a thin violet bar across its top. Panel headers are 30 pixels high, and the chat composer leaves a 10-pixel bottom margin when space permits. The same application state handles keyboard and mouse interaction independently of SDL and SkiaSharp.

The global chat and chats attached to graph members are **demos**: every prompt receives `Hello World!` after a short animated wait. Drafts and histories survive view/dialog switches during the session, but not process exit. The graph is an explicitly fictitious seven-card fixture with stable UUIDs, typed members, labeled relationships and simulated statuses. There is no inference, real source analysis, persistent graph or human status-validation workflow yet. No project files are edited or sent over the network.

See [the workshop prototype and rollback instructions](docs/design/workshop-prototype.md).

## Build and run

Requires a .NET 10 SDK compatible with `global.json`, network access for the first NuGet restore, an installed monospace font and a desktop session to open the window. SDL3 and Skia native libraries come from the NuGet packages. The runtime operates offline after restore.

```sh
dotnet restore Oryn.slnx
dotnet build Oryn.slnx --no-restore
dotnet run --project src/Oryn.Cli --no-build
```

`oryn` opens the directory it is launched from, including when installed elsewhere. `oryn ../another-project` opens an explicit directory. `oryn --check` reports project access and frontend configuration as JSON without initializing SDL or requiring a display. `oryn --help` lists the options. A Git repository is optional.

## Install the `oryn` command

For a user installation on **Linux** (Bash/Zsh), run from the repository:

```sh
bash scripts/install-linux.sh
export PATH="$HOME/.local/bin:$PATH"
oryn
```

The script detects x64/ARM64, publishes Release with the .NET runtime into
`~/.local/share/oryn`, and links `~/.local/bin/oryn`. It adds that directory to
`~/.profile`, `~/.bashrc`, `~/.zshrc` and any existing Bash login profiles without
repeating the same configuration line. The `export` above enables it in the current terminal; new Bash/Zsh
terminals use the persisted entry. For other shells, add that directory to their
PATH. The installer accepts musl runtime identifiers, but SDL/Skia native asset
availability for Alpine/musl has not been verified; glibc Linux is the tested target.
Use `--prefix /absolute/path` for another installation root and `--no-path` to
leave shell profiles unchanged; `--help` lists these options.

On **Windows**, run in PowerShell from the repository:

```powershell
.\scripts\install-windows.ps1
oryn
```

The script detects x64/ARM64, publishes to `%LOCALAPPDATA%\Oryn`, and adds that
directory to the user PATH and current PowerShell PATH without administrator
rights. New terminal applications pick up the persisted PATH; an existing
terminal host may need restarting. Options: `-InstallDir 'D:\Tools\Oryn'`,
`-Runtime win-arm64` and `-NoPath`. If PowerShell's execution policy blocks
the script, after reviewing it you can run it with a process-scoped override:
`powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-windows.ps1`.
In that case, reopen your terminal for the updated PATH.

Both installers require a .NET 10 SDK compatible with `global.json` and network
access for NuGet/runtime packs during publication. They use
[self-contained .NET publication](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
and keep the supporting files together; launching the installed application
does not require a separately installed .NET runtime. Run the installer again
to update Oryn after source changes, with Oryn closed. Launching from another
directory still opens that directory, without changing it to the install path.
The SDL frontend has been verified on Debian 13 x64. Windows/macOS native
execution and installer checks for this frontend remain pending. An SDL desktop
session is required to open the window; terminal-only SSH sessions can use `--check`.
Windows persists the PATH through the
[user environment](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_environment_variables).

For an alternative installation as a .NET tool, package and install into a
directory you control; this does not require publishing to NuGet:

```sh
dotnet pack src/Oryn.Cli -c Release -o artifacts/packages
dotnet tool install Oryn.Cli --version 0.1.0-m0 \
  --add-source ./artifacts/packages --tool-path ./artifacts/tools
```

Add the **absolute** path to `artifacts/tools` to your `PATH`, then launch `oryn` from any project directory. The .NET tool requires a .NET 10 runtime. On Windows, add the same tools directory to the user PATH.

Alternatively, publish a standalone executable for a specific platform:

```sh
dotnet publish src/Oryn.Cli -c Release -r osx-arm64 \
  --self-contained true -o artifacts/osx-arm64
```

The output executable is `artifacts/osx-arm64/oryn`. Keep its supporting files with it. Use the corresponding runtime identifier for other platforms; publishing alone does not verify graphical compatibility there.

## Controls

Oryn's configurable **Super** defaults to **Ctrl**. The native window receives keyboard/text events directly: Kitty mappings and terminal keyboard protocols are no longer involved. System-reserved shortcuts still depend on the desktop. The existing profile supports Ctrl/Alt/Shift or a leader such as Space/F12; direct Win/Command profiles remain unsupported in this tranche.

| Input | Action |
|---|---|
| Super+Tab / Super+Shift+Tab | Next / previous panel |
| Shift+Tab / F9 | Toggle top menu-bar selection |
| Left / Right or Tab in menu bar | Select File / Project / View / Agents / Tools / Settings |
| Enter in menu bar | Open selected menu; arrows then Enter select an action |
| Escape in menu / menu bar | Return to menu-bar selection / previous pane |
| Super+Enter / F6 | Enter central tab selection from any panel; press again to confirm |
| Left / Right or Tab in tab header | Select Chat / Code / Visualizer |
| Enter / Escape in tab header | Return to selected content |
| Tab in Chat | Switch composer / history focus |
| F1 / F2 / F3 / F4 | Files / Chat / Code / Visualizer |
| Up / Down in Files | Select entry |
| Enter in Files | Toggle folder or open read-only file preview |
| Right in Files | Expand folder; if expanded, select its first child; files stay selected |
| Left in Files | Collapse expanded folder; otherwise collapse and select its parent in one press |
| Enter / Shift+Enter in composer | Send / newline |
| Ctrl+A / Ctrl+C / Ctrl+X / Ctrl+V | Select all / copy / cut / paste in text fields |
| Arrows / Home / End in composer | Move caret; Shift extends selection |
| Super+K / F10 | Keybinding editor / workspace menu |
| Super+D | Toggle visualizer tab / dock |
| Super+Shift+Left / Right | Dock visualizer left / right |
| Super+Left / Right / F8 | Resize focused panel / reset widths |
| Super+Q | Quit |

Click tabs, files, cards and members to target them. Hold the left mouse button on a graph card to drag it freely: neighboring cards stay fixed, the moving card follows the cursor directly until its edge contacts an obstacle, then takes an animated collision-free detour with one-pixel clearance, and link endpoints follow its edges. Release keeps its current position; Escape during a drag restores the starting position. Positions are session-only, with no save/cache. Zoom or panel-width changes adapt the placement to the new card dimensions. In a narrow viewport, a detour may be clipped; selection is brought back into view on release. Wheel scrolls files, code and chat; horizontal wheel scrolls code. In the graph, wheel or +/PageUp and -/PageDown changes its Overview/Standard/Details presentation level; Home resets it. Enter enters a card's members, arrows navigate them, Escape returns to cards, and Enter on a member shows its demo details. Alt+Enter or right-click opens component options with **Show details**, **Show chat (demo)** and **Back**. The UUID-scoped chat remains isolated from parent, sibling and global conversations.

Drag panel separators to resize; Escape cancels the drag. Focus loss also cancels a drag. Small windows show one panel at a time, accessible with Super+Tab or F1–F4. The initial window is 1280×836 logical pixels with a minimum size of 360×264. Content is clipped to its panel and scales to the framebuffer on high-density displays. Layout currently keeps a 9×22 logical-pixel rhythm, rather than a terminal-cell output backend.

With Super=Alt, default panel traversal uses Ctrl+Tab/Ctrl+Shift+Tab to avoid the desktop-reserved Alt+Tab. Explicit custom shortcut conflicts are reported rather than silently overridden. Alt+Enter opens graph options in graph content; F6 remains available for tab selection. Leaders used as ordinary text are preserved while typing. File previews remain read-only; syntax highlighting is not implemented.

Keyboard actions that toggle focus, open dialogs or confirm a choice run **once per press**; holding arrows or Backspace still repeats navigation/editing. Enter held after confirming a tab does not also send a prompt or enter graph details. Returning to Chat with F2 or confirming its tab focuses the composer. Wheel does not move keyboard focus, and a pending file preview is cancelled when you navigate away.

Text fields own ordinary characters, caret navigation and Ctrl+A/C/X/V. AltGr-generated characters come from SDL Unicode text events; they are not treated as Ctrl/Alt shortcuts. A pending F12-style leader is resolved before editing, while Space/letter leaders remain literal text in a composer. Bare Tab stays within the active context. Ambiguous custom bindings are rejected with the conflicting scope/actions; to repurpose Tab or Ctrl+C, first unbind the local action. Dialog confirmation/cancellation uses unmodified Enter/Escape, and Shift+Tab reverses traversal inside dialogs; in workspace panels and tab headers it toggles menu-bar selection. Ctrl+C no longer quits by default; exact saved copies of the old quit defaults migrate in memory without rewriting the profile. The footer shows the current keyboard scope. The dispatcher compiles one command table per scope, including text fields and dialogs. See [ADR 0008](docs/adr/0008-scoped-keyboard-dispatch.md).

## Keybindings and configuration

Super+K opens the grouped action list. Click an action or use Tab/Shift+Tab to select a field; edit its comma-separated shortcuts. **Apply** changes the current session; **Save** also writes the profile atomically; **Reset** restores all defaults; **Cancel/Escape** closes without applying. Alt+A applies and Alt+S saves. The Navbar group configures menu-bar selection. File exposes Quit, View exposes dock/width actions, and Settings opens keybindings; other categories explicitly show that no actions are available yet. F10 retains the generic workspace menu. Text and Dialog groups configure editing, clipboard, modal navigation and keybinding-editor commands through the same router. Invalid bindings and conflicts leave the active router unchanged. Missing groups keep defaults; exact former Enter/Right explorer defaults migrate in memory to Enter plus the separate Right expansion action.

The default profile uses the platform application-data directory (`~/.config/oryn/keybindings.json` on Linux). A missing file loads defaults without creating it. `--keybindings /path/to/keys.json` selects an explicit profile; see [oryn.keybindings.example.json](oryn.keybindings.example.json). Profiles are never loaded automatically from the opened project.

Settings are explicit: `oryn --config /path/to/settings.json`; see [oryn.settings.example.json](oryn.settings.example.json). `--no-color` or `NO_COLOR` selects monochrome styling; `--ascii` replaces optional decorative Unicode glyphs. Chat labels use **Oryn** with a drawn star and **User** with a drawn point; the four-position Thinking animation is drawn with Skia shapes, independently of font glyph coverage in both modes. Filenames and source text retain Unicode. Text editing follows grapheme boundaries; complex-script shaping, bidirectional text and complete font fallback are not yet implemented. Glyph availability depends on installed fonts.

Hidden files are excluded by default. Each directory displays at most 1,000 entries and previews read at most 65,536 characters; truncation/errors are reported. Descendant symlinks are visible but not followed. Access stays within the project root. Composer input is bounded to 65,536 UTF-16 code units. Workspace state and conversations remain session-only.

## Rendering cadence

`--fps auto` (default) queries the **SDL window's display** every second and follows its reported refresh rate, falling back to 60 Hz. `--fps 144` selects manual pacing; valid rates are 1–500. `RenderFramesPerSecond: null` or a number in explicit JSON settings has the same meaning. `--check` reports the requested policy, not a detected desktop display rate.

Skia draws a raster bitmap and SDL presents it through a streaming texture. Skia redraws and uploads on changes or animation; SDL continues presenting the cached texture at the bounded cadence, including while idle, so the final interaction frame is not dependent on another input. VSync is disabled in this first adapter. SDL may choose an accelerated presentation backend, including OpenGL, while drawing remains SkiaSharp. This is separate from the direct OpenGL renderer planned for v1.0. Target Hz is not a claim of measured sustained FPS. Raster allocation is limited to 32 million pixels. No Niri IPC or terminal configuration is needed.

## Verification

```sh
dotnet test tests/Oryn.App.Tests --no-build --no-restore
dotnet run --project tests/Oryn.Smoke --no-build --no-restore -- \
  src/Oryn.Cli/bin/Debug/net10.0/oryn
dotnet run --project tests/Oryn.Ui.Smoke --no-build --no-restore -- /tmp/oryn-ui-captures
```

The UI smoke opens a temporary native window, injects SDL keyboard/text/mouse events, exercises previews, chats, graph navigation, keybinding save, compact resize, docking, drag cancellation and quit, and writes rendered PNGs to the explicit capture directory. It closes automatically. Without an output path, captures are temporary and removed after the test. For headless Linux verification:

```sh
SDL_VIDEODRIVER=dummy dotnet run --project tests/Oryn.Ui.Smoke --no-build --no-restore -- /tmp/oryn-ui-captures
```

For Linux/X11, the additional platform test sends events to its own X11 window before SDL translates them (40 consecutive Ctrl+Tab presses, tab selection and explorer expansion/collapse):

```sh
SDL_VIDEODRIVER=x11 dotnet run --project tests/Oryn.Ui.Smoke --no-build --no-restore -- --x11-keyboard
```

Current validation: build with zero warnings/errors, 120 App tests, CLI/domain smoke and both software/dummy and native Linux SDL smoke passed on Debian 13 x64. Captures were visually inspected. Windows/macOS, high-density hardware and native installer execution for the migrated frontend remain unverified. See [M0 verification](docs/m0-verification.md). Set `ORYN_DEBUG=1` for exception traces.

The old Python PTY scripts and `Oryn.Tui.Prototype` sources describe the historical terminal prototype. They are excluded from the current solution/CLI and are not tests of the SDL frontend.

## Architecture

- `Oryn.App`: workspace state, layout, commands, keyboard routing, graphical interaction controller, text editor and bounded async file services; no SDL, SkiaSharp or Terminal.Gui reference.
- `Oryn.Ui.Sdl`: SDL native lifecycle, event mapping, Skia painting and hit regions.
- `Oryn.Cli`: arguments, launch-directory policy and composition; still packages as a .NET tool.
- `Oryn.Ui.Smoke`: native event/render/lifecycle verification.

The original terminal ADRs remain historical. [ADR 0005](docs/adr/0005-sdl-rendering-direction.md) records the new frontend direction and v1.0 target; [ADR 0006](docs/adr/0006-sdl3-skia-raster-m0.md) records this implementation and its limits.
