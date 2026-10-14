# ADR 0004 — Grouped keyboard settings and Oryn Super

Status: implemented for M0 at the author's request.

## Context and decision

Oryn needs configurable workspace operations while preserving ordinary keys in
content and giving panel tabs their own focus. Use a framework-independent key
representation/router in `Oryn.App`. Group bindings into Workspace, Panels,
Tabs, Explorer and Content. Keep panel/content versus tab-header focus in
`WorkspaceState`, not in Terminal.Gui. The adapter translates input, invokes
typed actions and renders a header focus indicator.

Oryn Super defaults to Ctrl. Ctrl/Alt/Shift combinations are held modifiers;
Space, a letter or a function key acts as a leader followed by a command. Leader
sequences time out after a configurable bounded interval, can be cancelled with
Escape, and do not steal printable leaders from text entry. Ordinary Tab may
belong to editable content; Super+Tab remains workspace traversal. F6 provides
a default tab/content toggle for legacy terminals that cannot distinguish
Ctrl+Enter. Modal forms keep their own input; configured Quit remains available
while the workspace menu is active.

Validate keys/actions, required navigation/recovery bindings and conflicts in
overlapping scopes before applying a profile. Allow arrows to be reused across
mutually exclusive Explorer, tab-header and content scopes. Resize cancellation
has precedence over tab-header Escape. Unknown/expired leader sequences do not
execute arbitrary content commands.

## Settings and consequences

Provide a grouped form with session Apply, persistent Save, per-group reset and
Cancel. Draft edits cannot mutate active bindings. Validation and saving complete
before swapping the active router; failed writes leave the running profile and
previous file intact. Save an inspectable JSON profile with a temporary file and
same-directory atomic replacement. Load only the user application-data profile
or an explicitly supplied `--keybindings` file, never an opened project's file
automatically. Missing profiles use defaults without creating directories.

The renderer maps Ctrl, Alt and Shift. Terminal.Gui 2.5.0 does not expose a
System/Super modifier mask, so direct Win/Command configuration is rejected with
a visible explanation rather than silently translated. Terminal/compositor
reserved keys and legacy ambiguities cannot be solved by application remapping.
The input seam leaves room for future terminal/native support. No low-level input
reader, native module or additional assembly/package is justified for this slice.

The form requires 80×22; a smaller terminal receives an enlargement hint while
workspace navigation remains usable. The explicit profile file also supports
configuration outside the form. Workspace focus itself remains session state.

## Verification

Test group conflicts, aliases, held modifiers/leader timeout, literal text input,
tab passthrough, header/content transitions, resize cancellation, reset isolation,
atomic profile persistence and restart recovery independently of the renderer.
Exercise actual ANSI input and the form through POSIX PTYs; verify modal input
isolation, session-only Apply, persistent Save and configured navigation after
restart. Retain keyboard/mouse/layout/terminal-restoration checks.

Protocol context: [Kitty keyboard protocol](https://sw.kovidgoyal.net/kitty/keyboard-protocol/).
