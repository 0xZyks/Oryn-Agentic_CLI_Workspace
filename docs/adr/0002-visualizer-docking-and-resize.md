# ADR 0002 — Visualizer tab, docking and resizable panels

Status: implemented following the author's request.

## Decision

The visualizer has its own central tab and can move to a dock on either side of the center. Keep the initial right dock from the product specification. Clicking its tab moves the same view into the center; dock controls and shortcuts move it back. Preserve the most recent Chat/Code tab, visualizer view/scroll state and last dock side when switching placement.

Placement, preferred widths, focus order, splitter hit areas and resize transactions live in `Oryn.App`. The Terminal.Gui adapter maps mouse and keyboard input to that state. A tabbed visualizer is part of the Center focus group; left docking makes keyboard panel traversal follow the visual order. Render a single visualizer view and show it only in the active location.

Both side panels have draggable horizontal-size separators. Keyboard resizing changes the focused panel, with the center borrowing space from its neighbors. Clamp widths to retain a usable center and prevent overlap. Escape cancels a drag and restores its original preferences. Terminal resize ends a drag and recalculates visible bounds; stored width preferences remain intact. Compact mode uses a single panel and no draggable separators.

## Limits and verification

This extends the current side-by-side workspace, not a floating-window manager. Panel heights follow terminal height. Placement and sizes last for the current session; restart persistence remains a later milestone.

Core tests cover placement/focus, ordering and minimum widths across terminal sizes, hit testing, drag direction on either side, cancellation, keyboard resizing and reset. Real PTY tests exercise tab/dock transitions, both separator drags, keyboard shortcuts, compact/wide resize, cancellation and terminal restoration.
