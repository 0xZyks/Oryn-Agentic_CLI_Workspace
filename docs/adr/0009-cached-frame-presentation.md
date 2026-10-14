# ADR 0009 — Present cached frames while idle

Status: implemented in M0; physical-user confirmation pending.

## Context

The author reports that the last two typed characters appear only after two more presses, and that mouse and keyboard interaction remain inconsistent despite the scoped keybinding rewrite. They clarified that characters are delayed, rather than simply absent or malformed. This is compatible with a stale presented frame; if the screen trails the current application state, its visible controls can disagree with current hit regions. It does not establish an SDL peripheral-handling defect.

The adapter rasterized, uploaded and presented only on Dirty/animation. Previous smoke callbacks forced Dirty every frame to advance their scenarios, masking the idle behavior. Native bindings examined match the documented SDL keyboard/mouse/text structures; the installed packaged library reports SDL 3.5.0. No ABI mismatch was established.

## Decision

Separate raster drawing/upload from presentation. Redraw Skia only on changed state or visible animation; present the cached SDL texture every due frame at the existing auto/manual cadence. Sleep until the next presentation deadline with bounded event work. This avoids leaving the last state change dependent on a later input to advance a presentation pipeline, without repeatedly rasterizing or uploading idle content.

Cache the text-input rectangle and update it only on size changes or text-input restart, rather than issuing the same native IME update after every event and loop iteration. Preserve separate SDL Unicode input, keyboard routing and logical mouse coordinates. No keybinding rewrite or library upgrade is included.

## Verification and limits

The smoke no longer forces Dirty. Its 31 stages cover prior text/clipboard, mouse, graph, profile, resize and shutdown behaviors, plus individual Coucou character commits followed by six idle presentations with no intervening rasterization. Dummy/software and native Wayland/OpenGL passed; the idle Coucou raster capture was inspected. The X11 platform keyboard smoke also passed with 40 panel switches and no forced redraw. Build and self-contained Linux Release publication passed; software smoke using the exact published App/SDL assemblies passed.

An initial pre-change Wayland queue smoke failed its draft/clipboard assertion; that assertion subsequently passed, but this is not proof that presentation caused the original clipboard failure. Captures read the Skia bitmap, not compositor output. Synthetic inputs and frame counters verify application state, cadence and raster content; they do not independently prove the author's physical display now has no queued-frame lag. User confirmation remains necessary. Continuous bounded presentation costs more idle GPU work than event-only swaps while keeping idle rasterization/upload cached.

Primary reference: [SDL_RenderPresent](https://wiki.libsdl.org/SDL3/SDL_RenderPresent).
