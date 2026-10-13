# Oryn — M0 terminal workspace

Oryn is a local-first engineering workspace inside a real terminal. The product specification and complete roadmap are in [README_ORYN.md](README_ORYN.md); agent instructions are in [AGENTS.md](AGENTS.md).

M0 provides a file explorer, Chat/Code/Visualizer tabs, read-only file previews, a dockable visualizer placeholder, keyboard/mouse navigation and resizable panels. Chat inference and the graph are subsequent milestones. No project files are edited and no project data is sent over the network. Workspace persistence is not implemented in M0.

The workspace uses a Material-inspired dark theme: borderless surfaces, a lavender accent, flat clickable tabs, a project application bar and spaced panel headings. Terminals supporting true color show the full palette; monochrome/ASCII modes keep selection and focus indicators visible.

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

Package and install into a directory you control; this does not require publishing to NuGet:

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

| Key | Action |
|---|---|
| Tab / Shift+Tab | Next / previous panel |
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

Mouse selection, scrolling and menus are provided by the terminal adapter when supported. On terminals narrower than 90 columns or shorter than 12 rows, one panel is displayed at a time; switch with Tab or F1–F4.

Click the **Visualizer** tab to open it in the central area. Use **Open as tab**, **Dock again**, **Left** or **Right** in its toolbar, or the **View** menu, to change its placement. The same view moves between locations, preserving its content and scroll position. It initially docks on the right.

Drag the grips between panels horizontally to resize them. `Escape` cancels the current drag. Minimum widths keep the explorer, visualizer and central tabs usable. Manual widths are retained when the terminal shrinks and expands; compact mode hides the separators. Dock placement and widths are session state and reset on restart in M0.

## Configuration and limits

Settings are explicit: `oryn --config /path/to/settings.json`. See [oryn.settings.example.json](oryn.settings.example.json). Invalid or unknown settings produce an error. Oryn settings are not automatically loaded from opened projects; Terminal.Gui also supports its own `.tui` theme settings.

`--no-color` or `NO_COLOR` chooses a monochrome palette; `--ascii` uses basic decorations. Unicode filenames and source text retain their content. Terminal capability detection is conservative; advanced mouse/graphics support is not assumed.

Hidden files are excluded by default. Each directory displays at most 1,000 entries and previews read at most 65,536 characters; truncation is reported. Descendant symlinks are shown but not followed. Previews are read-only and control characters are sanitized. Filesystem errors are displayed without replacing the project root.

## Verification

```sh
dotnet test tests/Oryn.App.Tests --no-restore
dotnet run --project tests/Oryn.Smoke --no-restore
dotnet run --project tests/Oryn.Smoke --no-restore -- \
  src/Oryn.Cli/bin/Debug/net10.0/oryn
python3 scripts/terminal-smoke.py src/Oryn.Cli/bin/Debug/net10.0/oryn
```

The smoke executable starts the built CLI from a temporary directory **without passing a project path**, and verifies that it opens the launch directory. The Python script uses a real POSIX PTY to check both color/Unicode and ASCII/monochrome startup, browsing, focus, resizing, menus, exit and restoration. It requires Python 3 and is intended for macOS/Linux.

For manual terminal validation, launch Oryn, expand a folder, open a file, switch tabs and panels, resize below 90 columns, then quit. Check that the shell accepts input normally and its previous screen is restored. Set `ORYN_DEBUG=1` for exception traces when diagnosing an error. Primary development target: macOS ARM64; Windows/Linux terminal checks remain pending.

## Architecture

- `Oryn.App`: application state, commands, layout, startup options and filesystem services; no Terminal.Gui reference.
- `Oryn.Tui.Prototype`: temporary Terminal.Gui adapter and UI-thread updates.
- `Oryn.Cli`: executable, CLI arguments and composition; also packages as a .NET tool.

The accepted stack and launch-directory policy are recorded in [ADR 0001](docs/adr/0001-m0-terminal-workspace.md).
