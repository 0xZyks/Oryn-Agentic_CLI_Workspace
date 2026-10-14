# ADR 0010 — Session-only graph card dragging

Status: implemented as an M0 visualizer prototype.

Refinement (2026-10-14): remove anticipatory/magnetic motion. Free movement follows the pointer directly; swept contact precedes lateral routing. Clearance is now one pixel.

## Context

The author requests free left-button dragging, with fixed neighboring cards, no card overlap, sliding around the shorter side of an obstacle and rejoining the cursor. Link identities/endpoints must remain attached. Positions live only in session memory; no persistence or terminal-emulator work is requested.

## Decision

GraphDragState in Oryn.App owns floating-point scene positions keyed by existing UUIDs, pointer grab offsets, drag targets and bounded animated motion. SDL passes logical pixel coordinates; Skia consumes positions without owning them. Keep the initial layout as the placement seed, preserving graph entities, members, chats and relations.

Inflate fixed neighbors by the moving card footprint plus one logical pixel of clearance. Treat the moving card's top-left corner as a point, with swept segment/rectangle tests to prevent tunneling on large pointer jumps. Follow the requested displacement exactly until its first swept contact before starting any lateral detour. Clear paths immediately restore pointer/grab-offset tracking and clear the retained side. Route through visible exterior corners using a deterministic shortest path over the seven-node fixture. If the desired placement overlaps an obstacle, target its nearer lateral boundary; retain the chosen side while inside it to avoid center jitter. Advance along the collision detour at up to 1200 logical pixels/second with a bounded time step. Fixed cards are never displaced.

The scene allows negative coordinates so a dense starting arrangement can be contoured. Keep the camera stable during dragging so pointer coordinates do not change unexpectedly; selection visibility is restored after release. On small viewports parts of a path can leave the clip. Camera following/autoscroll and more sophisticated crowded-scene routing are future refinement, not claimed here. When no route/valid side is available the card stays at its last safe placement. Geometry changes end the drag and scale positions/card clearances to preserve disjointness.

Draw cards with fractional translations and recompute link attachment points on their rectangle edges, including arrow direction. Normal keyboard spatial navigation uses current positions. A click still selects cards/members; right-click keeps component options. Left release stops at the current position; Escape restores the starting position; focus loss, overlays or a keyboard workspace action end dragging. SDL's default automatic mouse capture covers held-button drags where supported.

## Verification and limits

110 App tests pass, including nearer-side detours in both directions, no per-step overlap, jitter stability, large jumps around multiple neighbors, unchanged UUIDs/relations, release/cancel, session isolation and zoom resizing. The 34-stage SDL smoke passes on dummy/software and native Wayland/OpenGL, exercising down/motion/up through the SDL event queue and checking no overlap in drag frames and pointer-target rejoin. Released card/link raster capture inspected. Linux Release packaging and smoke with the exact published App/SDL assemblies pass.

This is a small deterministic avoidance prototype, not a rigid-body simulation or a general-purpose graph-layout/physics engine. Link lines may cross other cards; their topology and endpoints stay intact. No persistence or native-language migration is added. Physical mouse feel and other platforms remain user/platform validation.

Reference: [SDL capture behavior](https://wiki.libsdl.org/SDL3/SDL_CaptureMouse).
