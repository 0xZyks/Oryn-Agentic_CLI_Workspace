# ADR 0003 — Monitor-aware terminal rendering cadence

Status: implemented for M0 at the author's request.

## Context

The primary Linux environment is Kitty on Niri/Wayland, with 60 Hz and 144 Hz
outputs. Oryn must choose its cadence before the first terminal frame and adapt
when its hosting window moves. A focused-window query is insufficient: focus
can belong to another application. Terminal.Gui's renderer remains provisional.

## Decision

Keep a renderer-independent cadence policy and display probe in `Oryn.App`.
Use Niri's read-only Unix-socket JSON IPC through .NET, with no native module or
new dependency. Match the terminal PID (`KITTY_PID`, then process ancestry) to
one compositor window; pin its ID and follow its workspace's output and current
mode. Multiple windows for the same process are ambiguous and fall back rather
than selecting the focused window. Requests/replies are bounded to 750 ms per
poll and 1 MiB per reply. No titles or monitor serial numbers are retained.

Auto is the default. Complete one probe before creating the TUI, then poll in
the background every second; apply changes on the UI thread. Errors reset Auto
to 60 Hz and later successful readings restore detection. Remote/multiplexed
sessions and unsupported platforms/compositors use that same fallback. Manual
configuration/CLI rates override detection; View menu choices apply for the
current session and can return to Auto.

The platform seam allows other probes later. A C/Rust bridge would add build
and packaging work without improving the existing Niri IPC path. Cross-platform
automatic monitor detection is not claimed for this slice.

## Alternatives and consequences

- Choosing the fastest connected screen, or the focused output, can select the
  wrong monitor. Do neither.
- An event stream could reduce polling, but separate output-mode observation and
  reconnection are still required. The bounded 1 Hz polling approach is simpler.
- System-specific Windows/macOS/X11/other Wayland adapters remain future work.

This sets an application loop target, not monitor VSync. Terminal.Gui 2.5.0
quantizes its loop budget to whole milliseconds (`1000 / rate`); at 144 Hz this
is 6 ms. Actual iteration and displayed frame rates can differ from the requested
rate. The terminal emulator may impose its own cap. Unchanged views still skip
drawing; monitoring does not force continuous redraws.

## Verification

Test PID matching without focus, ambiguous windows, pinned identity, current-mode
selection, 143.998/59.94 Hz rounding, monitor movement, invalid replies, disconnect,
timeout, cancellation, reply bounds and manual/Auto transitions. Exercise startup
and runtime changes through a real PTY with simulated IPC, and separately check
read-only detection against the user's actual Niri session. Desktop windows and
monitor configuration are never moved or changed by validation.

Protocol reference: [Niri IPC types and wire format](https://github.com/niri-wm/niri/blob/main/niri-ipc/src/lib.rs).
