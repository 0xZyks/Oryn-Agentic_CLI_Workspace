# ADR 0006 — SDL3 + SkiaSharp M0 implementation

Status: implemented on Linux; Windows/macOS execution pending.
Implements [ADR 0005](0005-sdl-rendering-direction.md).

## Context and choice

Preserve the M0 workspace and its terminal-inspired appearance while replacing the Terminal.Gui frontend. Use .NET 10, `ppy.SDL3-CS` **2026.1002.1** and SkiaSharp **3.119.1**, with explicit native Linux assets. The binding package includes SDL3 binaries for the desktop runtime identifiers; SkiaSharp supplies Windows/macOS assets transitively. Native assets must remain alongside the published executable. Package availability is not a claim of verified portability.

SDL owns the window, text/keyboard/mouse events, display querying and texture presentation. Skia draws into a BGRA raster bitmap; its pixels upload to an SDL streaming texture only on changed frames or visible animation. SDL may select an accelerated presentation backend (OpenGL on the tested Linux session); this is still a SkiaSharp drawing implementation, not the planned direct OpenGL renderer for v1.0.

## Boundaries and consequences

`Oryn.App` retains state and toolkit-independent commands. `GraphicalWorkspaceController` handles file-result dispatch, modal scope, drafts and keybinding edits; `TextEditState` edits bounded UTF-16 text on grapheme boundaries. `Oryn.Ui.Sdl` owns native lifecycle, painting and hit regions. The CLI starts SDL on the process main thread and no longer requires an interactive terminal. `--check` works without initializing graphics.

Use the existing integer layout units as a 9×22 logical-pixel grid to preserve the TUI rhythm, then scale rendering to the actual framebuffer size. This is an initial adapter strategy, not a requirement to retain terminal geometry forever. The monospace typeface is selected from installed system fonts. Complex-script shaping, bidirectional text and comprehensive glyph fallback remain limits of this first drawing adapter; UTF-8 input and grapheme-safe editing do not imply complete multilingual rendering.

File operations are async and cancelled when replaced; completed results apply in the UI loop. Global and UUID chats remain explicitly fictitious, session-only M0 state. Mouse interaction includes file selection/opening, tabs, graph members, scroll, context options and splitter drag/cancel. The keybinding editor preserves grouped actions and the existing profile format, supports Apply/Save and resets all defaults. The old Terminal.Gui project and PTY scripts remain historical sources, excluded from the active solution/CLI.

Auto cadence queries SDL's current window display once per second with a 60 Hz fallback. `--fps N` sets manual bounded pacing. VSync is disabled for this initial presentation path; reported target Hz is not a measured sustained FPS. No Niri IPC, Kitty shortcuts, ANSI or terminal mode changes are needed by the new frontend. A raster surface is bounded to 32 million pixels. Native resources are released on quit and exceptions.

## Alternatives and verification

A GPU-backed Skia surface would avoid raster uploads but add graphics-context integration before validating this migration. Keep it as a measured follow-up if raster rendering becomes a bottleneck. A direct OpenGL renderer remains the v1.0 target.

Verify App behavior with unit tests; use `Oryn.Ui.Smoke` to inject SDL events through the actual queue, capture rendered images, resize the window and check SDL shutdown. The dummy video driver enables headless software verification; run the same suite with the real desktop driver for native presentation. Validate packaging separately on each target platform. Primary references: [ppy bindings](https://github.com/ppy/SDL3-CS), [SDL renderer API](https://wiki.libsdl.org/SDL3/SDL_CreateRenderer), [Skia surface creation](https://skia.org/docs/user/api/skcanvas_creation/).
