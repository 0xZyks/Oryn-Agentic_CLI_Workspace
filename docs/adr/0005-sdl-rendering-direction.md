# ADR 0005 — SDL rendering direction

Status: accepted by the project author on 2026-10-14. The M0 implementation is recorded in [ADR 0006](0006-sdl3-skia-raster-m0.md).
Supersedes the Terminal.Gui frontend choice in ADR 0001 and the terminal-native frontend requirement in the original specification. Existing launch-directory and application-state boundaries remain applicable.

## Context

The M0 prototype uses Terminal.Gui with application state and commands in Oryn.App. The author explicitly chooses to replace the terminal interface with SDL + SkiaSharp and targets SDL + OpenGL for v1.0.

## Decision

- **v0.1:** SDL for window lifecycle and platform events; SkiaSharp for drawing the interface, text, code canvas and visualizer.
- **v1.0:** retain SDL and replace the SkiaSharp drawing layer with an OpenGL renderer.
- Keep the CLI launch entrypoint, local-first operation and framework-independent application state, identities, focus and commands. Adapt cell-based layout and viewport geometry to graphical coordinates.

SDL version, .NET bindings, initial SkiaSharp rendering backend and native distribution are implementation choices to verify during M0. The OpenGL version/profile and rendering details are deferred to the v1.0 milestone.

## Alternatives

Keeping Terminal.Gui would preserve the existing terminal experience, but does not follow the author's new frontend decision. Implementing SDL + OpenGL immediately would move the v1.0 rendering work into v0.1; SkiaSharp is the selected first drawing layer.

## Consequences and verification

The selected product interface is now a native graphical window. Terminal protocols, terminal restoration and terminal-cell sizing become historical prototype concerns. SDL + SkiaSharp does not itself supply replacements for the current Terminal.Gui widgets: text input, scrolling, menus and dialogs require explicit implementation or integration.

The existing executable still uses Terminal.Gui. No graphical runtime, performance or portability validation is claimed by this ADR. Preserve historical ADRs and terminal test evidence. During migration, verify window startup/shutdown, native resource cleanup, resize/display scaling, text entry, keyboard/mouse focus, explorer, tabs, chat and graph navigation. Test the actual native packages on each platform before claiming support. The OpenGL transition must preserve these behaviors and be measured separately.
