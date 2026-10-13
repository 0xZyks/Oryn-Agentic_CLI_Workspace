# ADR 0001 — M0 terminal workspace

Status: accepted by the project author for M0.

## Context

Oryn needs a usable terminal shell before inference and graph work. The development machine is macOS ARM64 with .NET SDK 10.0.100. The long-term renderer must remain replaceable without rewriting application state and input routing.

## Decision

Use C# with .NET 10 and Terminal.Gui 2.5.0, pinned explicitly. Start with three production projects: App, Tui.Prototype and Cli. Keep project root, explorer rows, expansion, selection, panel focus, active tab, commands and responsive layout in App, which has no TUI dependencies. Map terminal events and widgets through the prototype adapter. Use standard .NET APIs and explicit JSON configuration for M0. Test the core with xUnit; provide a dependency-free smoke executable for basic workflows and CLI launch-directory checks.

The entrypoint captures `Directory.GetCurrentDirectory()` before initialization. `oryn` opens that directory by default; an optional directory argument resolves against it. Never use the installation directory, executable path or repository lookup to select a default project. Support both a .NET tool named `oryn` and publishing an apphost executable. Installation must not change the caller's working directory.

Filesystem enumeration and previews run outside the terminal event loop. Apply completed results on the UI thread, cancel superseded requests, and bound directory entries and preview size. M0 does not follow descendant symlinks. Narrow terminals show one panel at a time; wide terminals show explorer, center and visualizer. Chat and graph remain explicitly labeled placeholders.

## Alternatives

- Ratatui/Crossterm offers explicit rendering and event-loop control, but a C# core would need a Rust bridge immediately. Revisit with the future native renderer.
- A homemade renderer adds terminal compatibility and Unicode work before the initial product can be used. Defer it as specified in the README.
- Separate domain assemblies for every future component add structure without implementation needs. Split when a later milestone justifies it.

## Consequences and validation

Terminal.Gui remains a provisional dependency with transitive packages. An early compilation and terminal smoke test must establish API and terminal compatibility. Roslyn, SQLite, inference providers and native modules are deferred to their respective milestones. M0 has no project database or restart persistence.

Terminal.Gui 2.5 deprecates its bundled TextView in favor of a separate full editor package. M0 uses TextView only for read-only previews and placeholders, with a narrowly scoped deprecation suppression inside the adapter. Revisit the editor when code editing is implemented; do not add a full editor dependency for M0.

The first macOS PTY smoke test exposed an exception in `Tabs.Dispose`. The visual refinement now uses two flat, interactive labels as tab controls with standard Terminal.Gui command/mouse bindings. Active-tab state stays in App; the adapter shows the selected content. This also removes the framed tab widget and its disposal workaround.

The same test found changed POSIX terminal speed fields after library cleanup on macOS. The adapter therefore captures `stty -g` before startup and restores that snapshot after library disposal when `stty` is available. This small POSIX guard remains outside the shared application; Windows uses the library's console lifecycle. The PTY test checks the complete terminal settings, not only keyboard flags.

Check application commands and layout without the TUI, file browsing and bounded previews, invalid paths/configuration, and launch from a directory unrelated to the binary. Exercise terminal startup, navigation, resize, compact mode and exit on the primary OS. Do not claim Windows/Linux compatibility until tested there.
