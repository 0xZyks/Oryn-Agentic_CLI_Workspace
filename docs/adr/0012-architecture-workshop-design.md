# 0012 — Architecture workshop visual prototype

Status: prototype, M0, 2026-10-14.

## Context

The SDL migration offers graphical freedom. The user requested a sober modern design retaining the TUI spirit, and clarified that Oryn supports engineering/architecture alongside code editors. The prior user-pushed state must remain recoverable.

## Choice

Keep toolkit-independent interaction state and the existing SDL/Skia rendering pipeline. Open the CLI in an overview of Architecture, with Discussion and read-only Sources as complementary views. Introduce restrained sans-serif headings, compact monospace technical content, rounded surfaces and curved links. Add a width-dependent inspector using existing UUID-targeted dialogs. Preserve existing keyboard scopes and session-only collision-aware card placement.

## Alternatives and consequences

An editor/IDE layout would emphasize the wrong workflow. A full knowledge/decision workspace would prematurely imply M2–M5 capabilities. This prototype instead changes existing M0 presentation and startup selection without new persistence or native dependencies. The CLI startup differs from the reusable WorkspaceState default. Inspector space reduces map width at standard/detail zoom, where ordinary graph navigation scrolls the viewport. Cards center only when viewport/zoom changes to avoid jumping on drag release.

## Verification

Application tests, offscreen workshop/animation checks and native SDL smoke cover keyboard, dialogs, inspector UUID routing, resize and dragging. Inspect screenshots at normal and compact sizes. Preserve the pushed baseline, source archive and verified installation copy; document rollback in docs/design/workshop-prototype.md.

## Visual refinement after user feedback

The user supplied a dark graphical reference but requested a stronger TUI character, with priority on the application frame rather than individual tab contents. This supersedes the sans-serif heading choice above: the interface now shares the monospace family, uses blue-black surfaces and nearly square outer panels, and localizes the focus accent to headers/tabs. The layout removes decorative empty rows and reduces outer margins/gutters while reserving two menu/header rows and two status rows. Existing tab behavior/content remains in scope as-is; source coloring, Git and terminal views are deferred.

A subsequent adjustment orders the tabs Chat, Sources, Visualiser and opens Chat at startup. Headers gain 8 pixels; the composer gains a 10-pixel bottom margin where space permits. Focus uses a brighter panel and a full-width 2-pixel top bar, with inactive content slightly dimmed. Pixel-accurate hit regions cover the extra header height and track the raised composer while dispatching existing grid-based actions.
