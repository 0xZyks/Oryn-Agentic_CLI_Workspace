# M0 verification

Development environment: macOS 15.6.1 ARM64, .NET SDK 10.0.100, Terminal.Gui 2.5.0.

## Executed checks

- Full solution build with warnings treated as errors: successful, zero warnings/errors.
- All 25 xUnit test cases invoked in-process with their assertions and per-case disposal: successful. Covers caller-directory resolution, arguments, focus/tabs, five terminal sizes, lazy expansion/collapse, file preview, ordering/hidden entries, truncation/control characters, path errors, symlinks, cancellation, configuration validation, docking, focus ordering, splitter hit testing, drag direction/cancellation, minimum widths, keyboard resizing and retained size preferences.
- Dependency-free smoke executable: successful. Tests the core workflow and runs the CLI from a temporary directory without passing a project directory.
- Real POSIX PTY scenarios in Unicode/color and ASCII/monochrome: successful. Checks startup, mouse focus, folder expansion/collapse, preview, panel switching, wide-to-narrow-to-wide resize, menu, normal exit, alternate-screen restoration and complete terminal settings restoration.
- Material refinement: borderless surfaces and true-color output checked on a true-color PTY; flat Chat/Code tabs exercised by mouse. Monochrome/ASCII keeps explicit active-tab and focus indicators. A captured terminal frame was rendered locally for visual inspection of spacing, palette and text clipping.
- Docking/resizing refinement: a real PTY opens the Visualizer tab, returns to Chat, docks right/left/right, drags both separators, resizes the focused panel with keyboard, cancels a drag with Escape and resets sizes with F8. Both color and monochrome runs restore the terminal successfully.
- CLI package creation and installation to a local tool directory: successful. The installed `oryn` command is checked from a directory unrelated to its installation.

## Environment limitations

NuGet DNS access is blocked inside the agent sandbox. Dependencies were restored by the user to `.local/packages` before compilation. VSTest cannot bind its local communication socket in this sandbox (`SocketException: Permission denied`), so the test methods were executed directly through a temporary runner in `.local/test-runner`. The normal supported test command remains `dotnet test tests/Oryn.App.Tests` outside that restriction.

MSBuild worker communication is also restricted; agent builds use `-m:1 -nodeReuse:false -p:UseSharedCompilation=false`. These switches do not change the product.

Windows/Linux execution and installation have not been tested. Terminal emulators other than the tested POSIX PTY may expose different keyboard/mouse capabilities. Full interactive human review remains useful for appearance and comfort.

The adapter retains the POSIX terminal restoration guard documented in [ADR 0001](adr/0001-m0-terminal-workspace.md). The framed Tabs widget has been replaced by flat controls, removing its disposal workaround. Chat, graph, inference, editing and workspace restart persistence are intentionally outside M0.
