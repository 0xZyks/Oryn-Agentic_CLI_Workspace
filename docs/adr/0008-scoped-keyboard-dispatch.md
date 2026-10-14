# ADR 0008 — Single keyboard dispatcher with explicit scopes

Status: implemented for M0. Supersedes ADR 0007.

Adjustment (2026-10-14): the author requested restoring one-press parent collapse with Left on a file. The table below reflects that preference; the initial reasoning and verification remain historical.

## Context

The author still found the keyboard unusable after the incremental SDL fix and requested a rebuild around expected behavior. The previous controller combined grouped router rules, hardcoded editor shortcuts, modal handling and a Tab fallback. Ctrl+Enter was deliberately ignored in Explorer, Right toggled an expanded directory closed, and Left on a child collapsed its parent immediately. Those behaviors disagreed with the requested workflow.

## Decision

Compile a single key-to-command table for each explicit context: Explorer, Tabs, TextEditor, History, Content, Graph, Menu, Details, ElementChat and Bindings. The controller derives its context once and dispatches through that table. Editor, clipboard, local Tab and modal actions are configurable alongside workspace commands. Remove the independent editor/modal/fallback key handlers.

Conflict checking uses those same tables. A profile cannot assign a shortcut to two active actions in one context; errors identify the context and actions, and the active profile remains unchanged. Two explicit policies remain: Escape first cancels an active splitter drag, and Alt+Enter opens graph options with Super=Alt. Older profiles may omit the Text/Dialog groups. Exact former Explorer OpenSelection defaults of Enter/Right migrate in memory to Enter; custom conflicting profiles require resolving their reported conflict, without silently discarding their bindings or rewriting the file.

Expected interactions:

| Context/input | Behavior |
|---|---|
| Normal panel, Ctrl+Tab / Ctrl+Shift+Tab | Next/previous visible panel, once per press; enter content focus |
| Normal panel, Ctrl+Enter / F6 | Focus central tab selection from any panel; when already there, confirm content |
| Tabs, Right/Left or Tab/Shift+Tab | Select next/previous tab, preserving tab focus |
| Tabs, Enter/Escape | Return to selected content |
| Explorer, Up/Down | Move selection only |
| Explorer, Right | Expand directory; on an expanded directory select first child; no file preview |
| Explorer, Left | Collapse expanded directory; otherwise collapse and select its immediate parent |
| Explorer, Enter | Toggle directory/open a file preview |
| Composer, arrows/Home/End | Move caret; Shift extends selection |
| Composer, Tab/Shift+Tab | Toggle composer/history focus inside Chat |
| Modal | Own its input; panel/tab shortcuts do not reach the workspace |

SDL maps navigation/control keys by scancode, preserving layout-derived printable keycodes and separate Unicode text input. Track pressed stroke identities per device/scancode to detect duplicate down events even if their repeat flag is absent. Releases clear only their own stroke; focus changes clear pressed state. Repeat policy belongs to the dispatcher. Enter/Escape/Tab and toggles are one-shot; movement/deletion may repeat. Preserve AltGr/MODE and GUI modifiers instead of interpreting them as ordinary arrows or Ctrl shortcuts. IME confirmation remains separate from prompt submission.

## Alternatives and consequences

Further patches to separate handlers would retain overlapping precedence and duplicate conflict rules. A compiled scope table makes ownership inspectable and independently testable without a rendering framework. New commands require explicit scope membership. Custom mappings that intentionally reused a local shortcut must unbind the other action before applying; ambiguous silent precedence is removed.

Rendering and the SDL/SkiaSharp architecture stay at M0. This adds no inference, persistence or analysis features.

## Verification and limits

104 App tests passed, including 300 panel/tab cycles, Ctrl+Enter from all panels, directory/child/file direction semantics, every modal scope, configurable editor commands and held-key confirmation isolation. Adapter checks include 100 down/up cycles, duplicate down events with no repeat bit, navigation scancodes with unknown keycodes, AltGr, IME, leaders and text ownership. The existing 29-stage SDL queue smoke passed with dummy/software and native Linux/OpenGL presentation, including clipboard, async previews, graph/chats, profiles, resize and shutdown.

A new Linux X11 smoke sends targeted XSendEvent sequences only to its own window before SDL translates them: 40 consecutive Ctrl+Tab presses, Ctrl+Enter directly from Explorer, tab selection/confirmation and Right/Left directory navigation passed. It waits for startup modifiers to clear, because focus acquisition can include the desktop's currently held modifiers. It uses no global keyboard injection or desktop-focus changes. The SDL queue smoke remains useful and supplements, rather than replaces, the platform check.

These tests do not replay the author's physical keyboard and do not establish Wayland physical-input or Windows/macOS behavior. No persistent keystroke or prompt logging is added. Self-contained Linux Release publication and a smoke using its exact packaged App/SDL assemblies passed.

Primary references: [SDL keyboard event](https://wiki.libsdl.org/SDL3/SDL_KeyboardEvent), [SDL keycode options](https://wiki.libsdl.org/SDL3/SDL_HINT_KEYCODE_OPTIONS), [native X11 window properties](https://wiki.libsdl.org/SDL3/SDL_GetWindowProperties).
