# Oryn — M0 terminal workspace

Oryn is a local-first engineering workspace inside a real terminal. The product specification and complete roadmap are in [README_ORYN.md](README_ORYN.md); agent instructions are in [AGENTS.md](AGENTS.md).

M0 provides a file explorer, Chat/Code/Visualizer tabs, read-only file previews, a dockable visualizer demo, keyboard/mouse navigation and resizable panels. Chat inference and the real project graph are subsequent milestones. No project files are edited and no project data is sent over the network. Workspace persistence is not implemented in M0.

Code previews use a dedicated darker canvas with internal padding, similar to a fenced Markdown code block. The viewport remains read-only and preserves source indentation and horizontal scrolling. Monochrome mode uses a simple frame to distinguish the canvas. Syntax/semantic highlighting is not implemented yet; language analysis belongs to M3, with C#/Roslyn as the candidate first integration.

The Chat tab includes a **demo conversation**: type a prompt and press Enter to send; Shift+Enter inserts a new line. The composer is a borderless surface with a discreet `>` prompt marker and one row of padding above and below the text. It starts at three rows and grows with multiline input and wrapping, while reserving space for history; longer drafts scroll inside it. User messages appear on the right; Oryn replies on the left with `Hello World!` for every nonempty prompt. The draft and history survive tab switches within the session, but are not saved after exit. This preview does not call a model. Tab cycles the input and history; Super+Tab changes panels, and Super+Enter/F6 toggles tab selection. Space used as a leader remains ordinary text in the input; use F6 to reach the tab header from there. The history wraps on resize and scrolls to the newest reply. Shift+Enter requires a terminal that distinguishes it from Enter.

Chat preview smoke test on POSIX: `python3 scripts/chat-smoke.py src/Oryn.Cli/bin/Debug/net10.0/oryn`.

After submitting, a compact Braille spinner (`⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏`) and `Thinking...` appear for 1.6 seconds before the demo reply. Unicode frames advance every 100 ms; ASCII mode keeps the four-point diamond at 200 ms. Input remains usable for the next draft, which is preserved when the reply arrives. A second submission waits until the current reply completes. The animation uses terminal cells at the terminal's font size.

The workspace uses a Material-inspired dark theme: quiet surfaces, a lavender accent, flat clickable tabs, a project application bar and spaced panel headings. A full-width lavender bar sits on a separate row **above the focused panel**, following its size during resizing. Its lower block glyph attaches the accent visually to the panel's upper edge. There are no focus borders around the sides or bottom. The active panel's background and text brighten slightly; inactive panels, previews, captions and selections use a darker, quieter palette while staying readable. The tabbed visualizer shares the center's appearance; a docked visualizer has its own. Focus changes do not shift content. Compact layouts reserve a row for the accent above the panel. Terminals supporting true color show the full palette; monochrome retains a bright focus bar and bold headings, and ASCII uses `=` instead of `▁`.

## Build and run

Requires a .NET 10 SDK, an interactive terminal, and network access for the first NuGet restore. After dependency restoration, the application operates offline.

```sh
dotnet restore Oryn.slnx
dotnet build Oryn.slnx --no-restore
dotnet run --project src/Oryn.Cli --no-build
```

Run the executable directly to open **the directory you launch it from**, even when the binary is installed elsewhere:

```sh
cd ~/Documents/randomDir
/absolute/path/to/Oryn-CLI/src/Oryn.Cli/bin/Debug/net10.0/oryn
```

An optional path opens another directory: `oryn ../another-project`. `oryn --check` reports the resolved project root and entry count as JSON without starting the TUI. `oryn --help` shows all options. No Git repository is required.

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
PATH. On Alpine/musl, specify `--runtime linux-musl-x64` or `linux-musl-arm64`.
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
Linux workspace execution has passed automated PTY checks on Debian 13 x64;
Windows terminal execution and native installer checks remain pending.
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

The output executable is `artifacts/osx-arm64/oryn`. Keep its supporting files with it. Use the corresponding runtime identifier for other platforms; publishing alone does not verify terminal compatibility there.

## Controls

Oryn's **Super** is an application setting, defaulting to **Ctrl**. Use
**Ctrl+K** or **Help → Keybindings** to edit grouped shortcuts (Workspace,
Panels, Tabs, Explorer, Content, Graph). The editor requires an 80×22 terminal.
**Apply** changes the current session; **Save** also persists the user profile;
**Reset group** restores only that group's bindings; **Cancel** discards edits.
In the editor, Alt+W/P/T/E/O chooses a group, Alt+U targets Super, Alt+B targets
the binding field, and Tab/Shift+Tab moves between the action list and that field.
Alt+A applies, Alt+S saves, and Escape cancels.
Conflicts in overlapping scopes and invalid/unsupported keys are reported before
applying. Save uses the user configuration directory, typically
`~/.config/oryn/keybindings.json` on Linux, or the platform application-data
directory on Windows/macOS. A missing profile uses defaults without writing it.

`Super: "Alt"` changes all `Super+…` bindings together. `Super: "Space"` uses a
leader: press Space, release it, then press the command key within one second.
Letters/function keys such as F12 can also be leaders. The interval is configurable
from 100 to 5000 ms. Escape cancels a pending leader; the status hint shows it.
Text entry keeps literal spaces/letters. Direct Win/Command modifiers are not
exposed by the current adapter; its error explains how to choose another binding
or use a terminal remapping. The terminal/desktop must transmit chosen shortcuts;
legacy terminals may not distinguish Ctrl+Tab or Ctrl+Enter.

Kitty reserves Ctrl+Tab and Ctrl+Shift+Tab for its own tabs by default, so those
keys do not reach Oryn even with the extended keyboard protocol. For Kitty,
run `python3 scripts/configure-kitty-shortcuts.py` and reload its configuration
with Ctrl+Shift+F5. The script backs up `kitty.conf` and adds two mappings scoped
to `var:oryn`; Oryn sets that variable during its TUI session and clears it on
exit. Kitty keeps its usual tab shortcuts outside Oryn. `--dry-run` previews the
block without writing, and `--config /path/to/kitty.conf` selects another file.

An explicit profile can be selected with
`oryn --keybindings /path/to/keys.json`; see
[oryn.keybindings.example.json](oryn.keybindings.example.json). Omitted actions
retain their defaults, and an empty array disables an optional action. Profiles
are not automatically read from opened projects. `--check` reports the profile
path and selected Super. [ADR 0004](docs/adr/0004-keyboard-scopes-and-super.md)
records the routing and persistence boundary.

The defaults below assume Super=Ctrl. The footer and **Help → Keys** show the
current profile. **Ctrl+Enter** (or F6) focuses the central panel's tab header;
Left/Right then selects Chat/Code/Visualizer. Press **Enter** to confirm that tab
and focus its content; Ctrl+Enter/F6 remains a confirmation fallback, and Escape
also returns to content. Tab/Shift+Tab cycles the selected tab within this header.
The selected tab is underlined while its header has focus,
and the status bar labels Tabs versus Content. Clicking a tab focuses its header;
clicking its content returns to content focus. Explorer and docked Visualizer
currently have no tab header of their own.

| Key | Action |
|---|---|
| Tab / Shift+Tab | Local controls within the current panel |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous panel (Oryn Super modifier) |
| Ctrl+Enter / F6 | Enter tab selection; also confirms when already in that mode |
| Enter in tab header | Confirm the selected tab and focus its content |
| Left / Right in tab header | Previous / next central tab |
| Escape in tab header | Return to content |
| Ctrl+K | Edit grouped keybindings |
| Up / Down | Select an explorer item or scroll the current text view |
| Enter / Right | Open a file or expand/collapse a selected folder |
| Left | Collapse the current folder, or return to its parent |
| F1 / F2 / F3 / F4 | Files / Chat / Code / Visualizer |
| Ctrl+D | Switch the visualizer between its tab and the last dock side |
| Ctrl+Shift+Left / Right | Dock the visualizer on the left / right |
| Ctrl+Left / Right | Shrink / grow the focused panel by two columns |
| F8 | Reset panel sizes, keeping the current dock side |
| F10 | Open the menu |
| Ctrl+Q | Quit |

Mouse selection, scrolling and menus are provided by the terminal adapter when supported. On terminals narrower than 90 columns or shorter than 12 rows, one panel is displayed at a time; switch with Super+Tab or F1–F4. Bare Tab stays inside the active panel. Saved copies of the former default panel bindings are updated in memory on load; custom remappings are preserved, and the file changes only on an explicit Save.

Click the **Visualizer** tab to open it in the central area. Use **Open as tab**, **Dock again**, **Left** or **Right** in its toolbar, or the **View** menu, to change its placement. The same view moves between locations, preserving its content and scroll position. At startup, the visualizer is available only as a central tab; Chat is active and no visualizer dock is shown.

The visualizer currently displays an **explicitly fictitious Oryn template**, not an analyzed graph of the opened project. Seven cards illustrate an entry point, classes, an interface and a planned component, with labeled links and four simulated design states. Click a card or use arrows while visualizer content is focused to select a node. The viewport follows selection, including in a narrow dock.

`+`/PageUp increases the presentation level; `-`/PageDown decreases it; Home resets it. **Overview** shows compact name/status cards, **Standard** adds the type and more spacing, and **Details** reveals example members. Selection survives level changes, tab changes, docking and resize. These three mock presentation levels help tune the M0 design; real project/module/type/function semantic zoom remains later work. Zoom shortcuts belong to the configurable **Graph** group and do not intercept code/chat or tab-header navigation. POSIX graph smoke test: `python3 scripts/visualizer-smoke.py src/Oryn.Cli/bin/Debug/net10.0/oryn`.

**Enter** enters the selected card and reveals its members. Arrows then move within that card; the targeted member has a `>` marker and a brighter background. **Escape** returns to card navigation and the previous presentation level. Clicking a visible member also targets it directly. Enter on a member opens its demo details.

**Alt+Enter** opens the options menu for the exact targeted card or member. **Show details** displays its type, simulated status, stable fixture UUID and parent. **Show chat (demo)** opens a separate conversation for that UUID, using the fake `Hello World!` response. History and unsent drafts survive closing/reopening the dialog during the session; parent, sibling and global chats are independent. Nothing is persisted on restart and no model is called: persistent contextual chats remain M4 work. These actions are configurable in the **Graph** keybinding group. With `Super=Alt`, Alt+Enter prioritizes component options in visualizer content; **F6** still selects its tabs, and Super+Enter retains its normal behavior elsewhere. The modal options menu supports Tab/Shift+Tab and Enter, plus Alt+D/Alt+C for details/chat. Component dialogs require a terminal of at least 45×16. POSIX interaction smoke test: `python3 scripts/component-smoke.py src/Oryn.Cli/bin/Debug/net10.0/oryn`.

Drag the grips between panels horizontally to resize them. `Escape` cancels the current drag. Minimum widths keep the explorer, visualizer and central tabs usable. Manual widths are retained when the terminal shrinks and expands; compact mode hides the separators. Dock placement and widths are session state and reset on restart in M0.

## Configuration and limits

Settings are explicit: `oryn --config /path/to/settings.json`. See [oryn.settings.example.json](oryn.settings.example.json). Invalid or unknown settings produce an error. Oryn settings are not automatically loaded from opened projects; Terminal.Gui also supports its own `.tui` theme settings.

`--no-color` or `NO_COLOR` chooses a monochrome palette; `--ascii` uses basic decorations. Unicode filenames and source text retain their content. Terminal capability detection is conservative; advanced mouse/graphics support is not assumed.

Hidden files are excluded by default. Each directory displays at most 1,000 entries and previews read at most 65,536 characters; truncation is reported. Descendant symlinks are shown but not followed. Previews are read-only and control characters are sanitized. Filesystem errors are displayed without replacing the project root.

## Rendering cadence

By default, Oryn detects the hosting terminal's monitor **before the first TUI
frame**. In a local Niri/Wayland session it matches the terminal PID to a unique
window, follows that window's workspace/output, and reads the output's active
refresh rate. Kitty's `KITTY_PID` and process ancestry provide the PID candidates.
The window is tracked even when another application has focus. A background
check every second updates the rate after monitor moves or mode changes.

Other compositors/platforms, ambiguous terminal windows, SSH/tmux/screen, and
failed or timed-out detection fall back to 60 Hz. The status bar shows Auto,
manual or fallback; **View → Refresh details** explains the current detection.
The initial probe waits at most 750 ms. Unsupported detection does not prevent
offline file browsing. Automatic detection on Windows/macOS is not implemented.

```sh
oryn --fps auto
oryn --fps 144
oryn --check
```

`--fps` accepts `auto` or an integer from 1 to 500, overriding explicit JSON
settings. `RenderFramesPerSecond: null` selects Auto; a number sets a manual rate.
**View → Refresh** choices switch Auto/60/120/144 Hz during the session without
restarting; these choices do not overwrite your configuration. Manual mode
disables polling until Auto is selected again. `--check` reports the initial
cadence, matched output/window and any detection failure without opening the TUI.

This configures the application loop, not monitor VSync. Terminal.Gui currently
uses whole-millisecond frame budgets, so the requested and actual cadence can
differ (144 requests a 6 ms budget). Views redraw when needed; visible refresh
also depends on rendering cost and the terminal emulator's own settings.
See [ADR 0003](docs/adr/0003-monitor-refresh-rate.md).

## Verification

To measure output cadence during a synthetic splitter drag on a POSIX PTY:

```sh
python3 scripts/render-benchmark.py src/Oryn.Cli/bin/Debug/net10.0/oryn
```

This reports output bursts and their median/p95 interval, plus active and idle
output volume. It measures application output, not frames displayed by the
terminal emulator; it does not guarantee a sustained 60 FPS.
The synthetic PTY has no desktop window and uses the 60 Hz fallback.

```sh
dotnet test tests/Oryn.App.Tests --no-restore
dotnet run --project tests/Oryn.Smoke --no-restore
dotnet run --project tests/Oryn.Smoke --no-restore -- \
  src/Oryn.Cli/bin/Debug/net10.0/oryn
python3 scripts/terminal-smoke.py src/Oryn.Cli/bin/Debug/net10.0/oryn
python3 scripts/refresh-rate-smoke.py src/Oryn.Cli/bin/Debug/net10.0/oryn
python3 scripts/keyboard-smoke.py src/Oryn.Cli/bin/Debug/net10.0/oryn
```

The smoke executable starts the built CLI from a temporary directory **without passing a project path**, and verifies that it opens the launch directory. The Python script uses a real POSIX PTY to check both color/Unicode and ASCII/monochrome startup, browsing, focus, resizing, menus, exit and restoration. It requires Python 3 and is intended for macOS/Linux.

For manual terminal validation, launch Oryn, expand a folder, open a file, switch tabs and panels, resize below 90 columns, then quit. Check that the shell accepts input normally and its previous screen is restored. Set `ORYN_DEBUG=1` for exception traces when diagnosing an error. Automated PTY checks have passed on macOS ARM64 and Debian 13 x64; Windows terminal checks remain pending. Monitor-aware startup/runtime changes have been tested on Linux with simulated Niri IPC, and initial detection separately against the actual Niri session.

## Architecture

- `Oryn.App`: application state, commands, layout, startup options and filesystem services; no Terminal.Gui reference.
- `Oryn.Tui.Prototype`: temporary Terminal.Gui adapter and UI-thread updates.
- `Oryn.Cli`: executable, CLI arguments and composition; also packages as a .NET tool.

The accepted stack and launch-directory policy are recorded in [ADR 0001](docs/adr/0001-m0-terminal-workspace.md).
