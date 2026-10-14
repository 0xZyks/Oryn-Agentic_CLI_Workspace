# ADR 0007 — SDL keyboard routing and focus

Status: superseded by [ADR 0008](0008-scoped-keyboard-dispatch.md). The validation below describes the earlier incremental fix, which did not resolve the author’s keyboard experience.

## Context

The first SDL migration passed isolated key-down smoke checks but the author reported inconsistent keyboard interaction. The adapter replayed state-changing commands on repeats, filtered text based on Ctrl rather than whether a shortcut was consumed, ignored releases, and retained hidden history focus. Local Tab and modal Enter/Escape fallbacks also accepted unrelated modifiers. Wheel and late file results could move keyboard focus unexpectedly.

## Decision

- Route application commands, local editing and clipboard requests in Oryn.App; SDL maps platform events and performs clipboard access. Return whether a stroke was consumed instead of guessing from modifiers.
- Keep modifier transitions separate from text-producing strokes. Track press/release ownership for suppressing shortcut text. AltGr/MODE has a distinct input marker; GUI modifiers are preserved rather than silently becoming unmodified keys. Committed IME text may follow its confirmation key without submitting a prompt.
- Apply toggles, confirmations, modal openings and panel traversal once per press. Repeat cursor movement, deletion, list navigation and explicitly repeatable navigation actions. Confirming an IME composition does not send Chat.
- Give a pending leader precedence over editing. Otherwise unmodified characters and caret navigation belong to editable text; local Ctrl+A/C/X/V remain editing commands in fields. Modal commands stay within the modal scope. Plain Enter/Escape/Tab fallbacks require their actual modifiers; Shift+Tab reverses traversal. Explicit profile panel-Tab remaps still apply across panels.
- Restore composer focus when returning explicitly to Chat. Wheel acts on the hovered view without changing keyboard focus. Explicit user navigation cancels pending previews so completed file reads cannot switch the user back to Code.
- Remove Ctrl+C from the default quit bindings; retain Super+Q. Migrate only the exact former default array in memory, preserving user files and custom bindings.

## Verification and limits

Fifteen App regression tests cover repeats, local/global/modal scopes, leader commands, profile migration, clipboard ownership, stable wheel focus and late preview cancellation. SDL smoke checks now include key-up, left/right modifier masks, lock/GUI flags, held shortcuts, AltGr, command text leaking into a newly opened field, IME commit and Equals versus Plus. The native event-queue suite exercises these sequences with the real Linux presentation backend as well as the dummy driver.

These checks inject SDL events; they do not record the author's physical keyboard or establish macOS/Windows key-layout behavior. The desktop can reserve shortcuts (especially Alt+Tab), and direct Win/Command profiles remain unsupported. No key-event or prompt-content diagnostic logging is added.

Primary references: [SDL keyboard-event structure](https://wiki.libsdl.org/SDL3/SDL_KeyboardEvent), [keycode representation/layout options](https://wiki.libsdl.org/SDL3/SDL_HINT_KEYCODE_OPTIONS), [Unicode text input lifecycle](https://wiki.libsdl.org/SDL3/SDL_StartTextInput).
