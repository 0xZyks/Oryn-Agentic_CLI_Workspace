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

## Linux rendering cadence — 2026-10-13

On Debian 13 x64, SDK 10.0.401, the adapter now explicitly sets
`Application.MaximumIterationsPerSecond = 60` after initialization. The installed
Terminal.Gui 2.5.0 source defaults to 25; its main loop includes input processing
and drawing, and skips drawing unchanged views. No periodic forced redraw was added.

`scripts/render-benchmark.py` drives an explorer splitter at 120 Hz for three
seconds on a 120×30 POSIX PTY, grouping output chunks less than 4 ms apart.
Before/after measurements of the Debug binary on this machine:

| Measurement | Default 25 | Target 60 |
|---|---:|---:|
| Output bursts/s | 23.7 | 47.7 |
| Median interval | 40.20 ms | 16.36 ms |
| p95 interval | 59.09 ms | 52.99 ms |
| Active output over 3 s | 582,588 bytes | 938,046 bytes |
| Idle output/s | 308 bytes | 764 bytes |

Output bursts approximate redraw cadence, not frames presented by a terminal
emulator. Integer character positions and repeated drag positions also affect
the count. The faster median demonstrates that the 40 ms loop limit was removed;
the long-tail intervals remain and a sustained visible 60 FPS is not established.
The after measurement ran alongside the functional terminal smoke. CPU usage
was not measured; the higher loop rate can increase CPU and terminal output.

## Monitor-aware Auto cadence — 2026-10-13

The fixed 60 Hz setting above is now the fallback. Auto completes a bounded
Niri probe before the TUI is created, then follows the hosting window's output
with background polling every second. Manual rates are available through
`--fps`, explicit JSON settings and the View menu. See [ADR 0003](adr/0003-monitor-refresh-rate.md).

Executed on Debian 13 x64, SDK 10.0.401:

- Solution build: zero warnings/errors.
- Standard xUnit runner: 42 tests passed. Includes IPC transport, pinned window
  identity, ambiguous PID, active mode, fractional rate rounding, movement,
  timeout, cancellation, oversized/malformed replies and manual/Auto policy.
- CLI/workspace smoke: passed, including the caller-directory check.
- Existing PTY smoke: Unicode/color and ASCII/monochrome passed, including
  navigation, docking, resizing and terminal restoration.
- `scripts/refresh-rate-smoke.py`: passed. A simulated Niri server and a real
  PTY verify initial 144 Hz, moves to 60 Hz and back, View menu manual/Auto,
  IPC failure/recovery, exit and terminal restoration. This does not move any
  desktop windows or modify monitor modes.
- Actual Niri read-only diagnostic: the inherited Kitty PID matches window 2,
  whose workspace maps to HDMI-A-2 at 60 Hz. Both connected outputs were
  independently read: HDMI-A-2 at 60 Hz and HDMI-A-1 at 143.998 Hz.
- Final synthetic PTY benchmark at the 60 Hz fallback: 47.3 output bursts/s,
  median 16.16 ms, p95 54.60 ms, 910,816 bytes/3 s, idle 752 bytes/s.

VSTest and simulated Unix sockets ran outside the sandbox, which blocks local
socket access. Initial native detection was checked against Niri outside the
sandbox as well. Windows/macOS monitor detection and genuine cross-monitor
window moves remain unverified; the automated movement tests simulate the IPC
state. No native library, additional NuGet dependency or forced redraw timer.

## Focus halo — 2026-10-13 (historical; superseded by the focus accent below)

The active panel now has a rounded lavender outline, softer vertical sides and
a brighter heading/selected tab. The outline is decorative and uses existing
padding: viewport sizes, mouse coordinates and application focus state are
unchanged. A tabbed visualizer uses the whole central panel outline; a docked
visualizer uses its own panel. Only one outline is present. ASCII/monochrome
provides a bright `+`/`-`/`|` fallback; panels shorter than seven rows use their
heading marker to preserve compact controls.

Executed on Debian 13 x64, SDK 10.0.401:

- Solution build passed with zero warnings/errors.
- CLI/core smoke passed, including opening the caller's directory.
- Enhanced PTY smoke passed in Unicode/color and ASCII/monochrome: outline at
  startup, focus between Explorer/Chat, tabbed and docked Visualizer, compact
  resize, tiny 30×6 terminal fallback, return to wide layout, previews, mouse,
  panel resizing/cancellation, menu, exit and complete terminal restoration.
- Captured color and monochrome frames were replayed into a temporary SVG/PNG
  preview for visual inspection of outline placement and content clearance.
  That preview approximates font metrics; it is not a screenshot of the user's
  terminal emulator. Temporary captures/previews remain outside the repository.
- `git diff --check` passed.

The application domain and cadence policy are unchanged. The 42 xUnit tests
from the preceding change were not repeated for this adapter-only visual change.
Windows/macOS appearance has not been exercised in this session.

## Full-width focus accent and panel contrast — 2026-10-13

The halo is removed. One decorative bar sits at the focused panel's X position,
one row above its top, with exactly its width. Unicode uses `▁`, anchoring the
stroke at the bottom of that row against the panel; ASCII uses `=`.
It follows focus, docking and resizing, with no side or bottom outline. Compact
layout now reserves a second top row to keep the bar below the app bar and above
panel content. Short-panel controls use a tighter vertical arrangement.

Panel backgrounds, normal/muted text, headings, tabs and explorer selection all
use focused/unfocused palettes. The active panel is slightly brighter; the other
panels are quieter. This is a presentation policy applied to existing widgets,
not an input-intercepting overlay. Palette changes are cached by focus/placement,
and bar geometry is cached independently. Monochrome preserves its readable
black/white palette and relies on the bright bar and heading emphasis.

Executed on Debian 13 x64, SDK 10.0.401:

- Solution build: zero warnings/errors.
- All 42 xUnit tests passed through the standard runner outside the sandbox,
  including terminal layout bounds after the compact top-row adjustment.
- CLI/core smoke passed, including the caller-directory check.
- Updated PTY smoke passed in Unicode/color and ASCII/monochrome. Checks full
  accent widths at startup, center focus, compact mode and docked Visualizer;
  live resizing of the accent; focused/unfocused background and text colors;
  30×6 terminal layout; previews, mouse, keyboard, menu, exit and restoration.
- Captured ANSI frames replayed into temporary SVG/PNG previews confirmed the
  bar above the panel, absence of the old contour, and contrasting pane/text
  appearance for Explorer and Chat focus. Font metrics remain approximate.
- `git diff --check` passed.

The first new resize assertion looked only at mouse release output; the bar had
already resized during mouse movement. The harness was corrected to examine
both outputs, then both full terminal scenarios passed. No product workaround
was needed. Native Windows/macOS appearance and packaging were not exercised.

The final positioning refinement replaced the upper-block glyph with the lower
block, avoiding the appearance of a navbar underline. Solution compilation and
both complete PTY scenarios passed again; a replayed color frame was inspected.
Panel geometry, palette and cadence were unchanged for that refinement.

## Grouped shortcuts and tab/content focus — 2026-10-14

Oryn Super defaults to Ctrl and is configurable as held Ctrl/Alt/Shift
combinations or a leader such as Space/F12. Profiles group Workspace, Panels,
Tabs, Explorer and Content actions, validate overlapping-scope conflicts and
persist through explicit Save. Missing actions keep defaults. Modal editors keep
their own input. The tab header and content focus are modeled independently of
the renderer; the header's active tab is underlined and the status labels the mode.

Executed on Debian 13 x64, SDK 10.0.401:

- Build of the full solution: zero warnings/errors.
- Standard xUnit runner: 55 tests passed, including scoped arrows, Super and
  ordinary Tab, text-entry passthrough, leader timeout/cancellation, unsupported
  modifiers, conflict validation, group reset isolation, persistence and domain
  tab/content focus transitions. VSTest ran outside the sandbox for its socket.
- CLI/core smoke: passed, including launch-directory behavior.
- Existing Unicode/color and ASCII/mono PTY smoke: passed, including focus
  appearance, keyboard/mouse, docking, resizing, compact/tiny layout, configured
  menu activation, quit after closing the menu, and terminal restoration.
- `scripts/keyboard-smoke.py`: passed. Actual ANSI input checks Ctrl, Alt and
  Space profiles; panel traversal; Ctrl/Alt/leader+Enter tab focus; arrows selecting
  tabs; content arrows retaining the active tab; F6 fallback; the grouped form;
  session Apply without a file write; persistent Save; live remapping; per-group
  reset; restart recovery; and menu close followed by configured panel traversal.
  The form is exercised with Tab from the action list to the binding field.
- `oryn --check --keybindings oryn.keybindings.example.json`: passed, reports
  the explicit profile path and Ctrl Super. Niri IPC is unavailable inside the
  sandbox and correctly reports the cadence fallback in this diagnostic.
- `git diff --check`: passed.

Validation exposed a native list/form Tab focus issue and menu focus that could
block Quit. The editor now explicitly routes list→field Tab / Shift+Tab and
defers list focus after group activation; workspace commands remain accessible
from the menu. Save assertions inspect the persisted profile and exercise the
new shortcut, rather than expecting unchanged footer text to be emitted again.

All profile writes during tests target temporary paths, not the user's settings.
The editor requires 80×22; narrower terminals show an enlargement hint and retain
workspace navigation. Direct Win/Command is not exposed by this adapter and is
rejected visibly; OS/terminal-reserved keys and legacy Ctrl+Enter/Tab ambiguities
remain environment-dependent. Windows/macOS execution and packaging were not
retested. [ADR 0004](adr/0004-keyboard-scopes-and-super.md) describes the boundary.

### Chat layout preview — 2026-10-14

M0 now includes a session-only demo chat. Enter or Send submits a nonempty
prompt, appends the user message on the right, and appends `Hello World!` from
Oryn on the left. No inference, networking or persistence is involved.
The draft survives tab changes; the input and Send stay below the scrollable
history. Tab cycles chat controls, Super+Tab traverses panels, and Space leaders
do not intercept prompt spaces. The composer hides below four panel rows.

Executed on Linux:

- Solution build: passed with zero warnings/errors.
- App unit tests: 58 passed, including empty prompt, ordered messages, draft
  clearing and preservation across tabs. VSTest needed its local socket outside
  the sandbox.
- `scripts/chat-smoke.py`: Unicode/color and ASCII/mono passed, including Enter,
  keyboard Send, mouse Send, draft recovery, cursor editing, tab focus, narrow
  wrapping, repeated sends and terminal restoration. Space leader typing passed.
- Existing keyboard and terminal PTY suites passed during this change.
- PTY transcript reconstructed into SVG/PNG and visually inspected for left/right
  alignment and the visible Send control; this is not a real terminal screenshot.
- `git diff --check`: passed.

Windows/macOS and an actual LLM provider remain untested in this preview.

### Multiline composer — 2026-10-14

The composer replaces Send with an input-only rounded frame, up to seven rows
high. Its text block is vertically centered while short, then scrolls inside the
frame when longer. `>` identifies the beginning of the prompt. Enter submits;
Shift+Enter inserts a newline when the terminal reports the modifier. ASCII
uses square corners. Small panels shrink the frame; below five rows it hides.
Tab/Shift+Tab alternate input and history. The editable color role now uses the
same surface background as the surrounding composer.

Build passed with zero warnings/errors. Updated chat PTY checks cover a two-line
draft surviving tab switches and being sent as one prompt, cursor editing,
mouse focus, Tab, narrow resize, multiple replies, Space leader and restoration
in Unicode/color and ASCII/mono. The keyboard suite passed again for all three
profiles. The terminal suite passed during this change after replacing its old
blanket prohibition of rounded corners with the new composer expectation.
The final color adjustment was checked through chat PTY and a reconstructed
SVG/PNG. The 58 App unit tests from the preceding prompt were not rerun; App
behavior is unchanged. Windows/macOS remain untested.

### Compact borderless composer — 2026-10-14

The composer now starts at three rows: one text row and one padding row above
and below. It grows with explicit newlines and wrapping, reserving history
space (at most half the panel) before scrolling. Rounded borders are removed;
the surface and `>` remain. Padding is cell-based, so its pixel height depends
on the terminal font. Build and chat/terminal PTY checks passed in Unicode/color
and ASCII/mono, including multiline draft recovery, sending, cursor editing,
mouse focus, resize and terminal restoration. A reconstructed two-line draft
was visually checked for one padding row on each side and absence of a border.
App behavior is unchanged; prior unit/keyboard results remain historical.

### Thinking preview — 2026-10-14

Submitting now appends the user message and enters a pending state. A four-point
ASCII diamond rotates its star every 200 ms beside `Thinking...`; after 1.6 s
the preview completes with `Hello World!`. The application timeout keeps input
and navigation usable, and completes the reply even while Chat is hidden.
The next draft is preserved; another submission is ignored while pending.
There is still no model, networking or persistence.

Build passed with zero warnings/errors; 59 App tests passed, including pending
submission guards, draft preservation and idempotent completion. Chat PTY checks
passed in Unicode/color and ASCII/mono, plus Space leader. An additional PTY
probe verified frame progression, navigation during waiting and reply completion
while Code was visible. A reconstructed thinking frame was visually inspected.
The previous general terminal/keyboard suites remain historical for this prompt.
Font size is controlled by the terminal; no smaller font is claimed.

### Code canvas — 2026-10-14

Code previews now sit inside a separate darker surface, with internal padding
and the existing read-only, non-wrapping viewport. It follows panel dimensions
and focus contrast; monochrome uses a simple frame. No parser/LSP or syntax
highlighting was added. The language-analysis roadmap remains M3.

Solution build passed with zero warnings/errors. The general terminal PTY suite
passed in Unicode/color and ASCII/mono, including code preview/tab restoration,
canvas background, focus, resize, docks, compact/tiny layouts and restoration.
The keyboard suite passed for Ctrl/Alt/Space and the settings editor. A color
canvas capture was reconstructed and visually inspected. App unit tests remain
historical because this change touches presentation only. Windows/macOS were
not executed.

### Navigable visualizer fixture — 2026-10-14

The placeholder now renders seven fictitious Oryn-inspired cards, six labeled
links and four simulated design states. `VisualizerPreviewState` owns UUIDs,
selection, detail preset and viewport; `PreviewGraphLayout` owns cell geometry,
independently of Terminal.Gui. The adapter composes toolkit Views/Labels,
including native clipping and viewport offsets. No project graph is analyzed
or persisted, and the fixture states are not approvals about Oryn.

Arrow keys select nodes while graph content is focused; mouse clicks select
cards. Plus/PageUp and Minus/PageDown switch Overview/Standard/Details; Home
resets Standard. Selected UUID stays unchanged, and the viewport brings the
selected card into view after navigation, density change or resize. Tiny
viewports show its top first. Zoom shortcuts have a configurable Graph group,
scope excludes code/chat and tab headers, and settings accept older profiles
with the new group omitted.

Executed on Linux:

- Solution build passed, zero warnings/errors.
- App unit tests: 63 passed, including fixture UUID/link consistency, navigation,
  bounded presets, viewport resize and scoped/conflicting graph bindings.
- `scripts/visualizer-smoke.py`: Unicode/color and ASCII/mono passed; cards,
  mouse/arrows, detail members, +/- and PageUp/PageDown, Graph settings group,
  tab-header navigation, docking, narrow viewport and restoration.
- Existing keyboard suite: Ctrl/Alt/Space and grouped settings passed.
- Existing terminal suite: Unicode/color and ASCII/mono passed during the change,
  including the demo replacing the former visualizer placeholder.
- Example keybindings profile validated with `oryn --check --keybindings`.
- Reconstructed PTY image inspected for cards, labels and connections.
- `git diff --check`: passed.

This implements mock presentation levels, not real semantic graph depth/CFG.
Free panning, wheel zoom, more element kinds and full language analysis remain
future work. Windows/macOS and packaging were not executed.

### Panel traversal uses Super+Tab — 2026-10-14

Default NextPanel/PreviousPanel bindings now use only Super+Tab and
Super+Shift+Tab. Bare Tab/Shift+Tab move among local controls in the active
panel, with wrapping restricted to that panel; native toolkit focus traversal
cannot escape to another pane. Explorer/Code keep their single content control,
Chat alternates composer/history, tab headers cycle their controls, and the
visualizer cycles graph/visible dock controls. Menus and dialogs retain native
local navigation; explicit custom bindings remain configurable.

Saved exact copies of the previous default pairs are normalized on load in
memory, releasing bare Tab. Other remappings are preserved and the profile is
not rewritten until Save. CLI help, README and example profile were updated.

Build passed with zero warnings/errors; 64 App tests passed, including local
Tab passthrough and non-writing migration of legacy defaults. Keyboard PTY
checks passed for Ctrl/Alt/Space, including bare Tab/Shift+Tab staying in Files,
Chat and Code. Graph and general terminal suites passed in Unicode/color and
ASCII/mono, including local graph Tab and Super+Tab panel traversal. CLI help
was executed and checked. Windows/macOS remain untested.

### Kitty interception of Ctrl+Tab — 2026-10-14

The user's physical Ctrl+Tab stayed immobile despite successful PTY routing.
Kitty's installed default configuration reserves Ctrl+Tab/Shift+Tab for its own
tabs. Injecting CSI-u into a PTY bypasses that interception, so the prior tests
did not cover the emulator shortcut layer.

`KittyShortcutScope` now sets `oryn` via OSC SetUserVar during the TUI session,
then clears it after the toolkit shuts down. The opt-in setup script backs up
Kitty configuration and appends two passthrough mappings conditioned on
`var:oryn`; other Kitty bindings remain active outside Oryn. Temporary config
tests verified dry-run, preservation, permissions, backup and idempotency.
Kitty's own parser accepted and finalized both scoped mappings.

Build and self-contained Linux Release publication passed. PTY checks on the
staged and actually installed Release executable verified scope marker cleanup,
Ctrl+Tab and Ctrl+Shift+Tab press/release routing and terminal restoration.
The approved setup updated the user's Kitty file, backed it up, atomically
replaced the installed terminal adapter and reloaded Kitty via SIGUSR1.
The existing Oryn process must be restarted to activate its new scope marker.
Actual physical keystrokes in the user's window remain for the user to confirm.

### Enter confirms tab selection — 2026-10-14

Super+Enter/F6 enters tab-header navigation; Enter now confirms the selected
tab and focuses its content. Super+Enter/F6 also remains a confirmation
fallback, and Escape still returns to content. `ConfirmTab` is a configurable
Tabs action, scoped to header navigation; Enter in Chat remains prompt submission.
Local Tab/Shift+Tab in the header now changes the application selection so the
highlighted tab and the tab confirmed by Enter stay aligned.

Build and self-contained Release publication passed. App tests: 65 passed.
Keyboard PTY checks passed for Ctrl/Alt/Space, including Enter confirmation,
Escape fallback and local header Tab/Shift+Tab. Graph suite passed in color and
ASCII/mono. A Release probe confirmed Enter for Chat/Code/Visualizer and prompt
submission after Chat confirmation. The authorized installed application/adapter/
CLI update was verified through a second PTY probe on the installed executable.
Kitty scope clearing and terminal restoration passed. Restart Oryn to use the
updated installed binaries. Windows/macOS were not executed.

### Component navigation and menus — 2026-10-14

Enter enters the selected fixture card; arrows then target its individual
members, Escape restores card navigation and the prior density. A visible
member can also be clicked. Enter on a member opens details. Alt+Enter opens
options on either level. Details include the exact fixture UUID and parent;
Show chat opens a session-only demo conversation keyed by that UUID.
Parent, same-named members of different parents, siblings and global chat do
not share histories. Drafts remain when a demo dialog is reopened. No model,
project analysis, database, or actual design-status transition is involved.

Build passed without warnings/errors; App tests: 68 passed. Keyboard suite
passed Ctrl/Alt/Space and the grouped editor; visualizer suite passed color
and ASCII/mono with existing zoom/dock/resize checks. Component PTY suite
passed Ctrl and Alt profiles: card/member menus, UUID details, Enter/Escape,
member mouse selection, sent history and unsent draft reopening, separate
scopes, 45×16 chat submission, and terminal restoration. Global chat smoke
and staged/installed Release results are recorded in the session handoff log.
An ANSI reconstruction of the member selection was visually inspected.
Windows/macOS have not been executed. Persistence and real contextual chats
remain subsequent milestones. With Super=Alt, GraphOptions has contextual
priority over ToggleTabFocus on Alt+Enter; F6 retains header navigation.
