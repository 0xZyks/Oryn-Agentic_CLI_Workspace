# Architecture workshop — M0 prototype

Oryn assists engineering reasoning and architecture alongside existing editors. The current visual pass focuses on the application frame: monospace typography throughout, blue-black surfaces, thin nearly square panel borders, compact menu/tab/status bars and localized lavender focus markers. Two rows at the top and two at the bottom leave the rest of the window to the panels, separated by one-column gutters.

The supplied visual reference informs the palette and density. The user's clarification prioritizes a stronger TUI character and the application frame; source coloring, symbol/reference views and redesigning individual tab contents remain separate work. Git and terminal views are outside this pass. Existing curved graph relationships and the contextual inspector remain from the preceding prototype.

The CLI starts in Chat. The tabs appear as Chat, Sources and Visualiser, matching keyboard cycling; source previews are read-only. Panel headers have 8 extra pixels (30 total), with matching mouse regions. The focused panel is brighter, inactive content is lightly dimmed, and a 2-pixel violet bar spans the active panel's top edge. The chat composer leaves a 10-pixel margin below it when the view is tall enough. On wide maps the inspector opens existing UUID details/discussions. It disappears on small windows without removing keyboard access through the component options. All displayed graph statuses and relations remain the fictitious M0 fixture. No inference, persistence, terminal emulator or new analysis capability was added.

## Baseline and rollback

The clean, user-pushed baseline is `203b03f97193cec3a4bd9f8f98f06c0ff49aee33`. Before editing, sources were archived and the full local installation copied to `.local/design-baseline-20261014-205948/` (ignored by Git). The manifest records installation SHA-256 hashes. No automatic commit or push accompanies this prototype.

Close Oryn, then preview the installation rollback from the repository:

```sh
python3 scripts/restore-design-baseline.py .local/design-baseline-20261014-205948
```

Add `--apply` to restore the six changed CLI/App/SDL DLL/PDB files. The script validates baseline hashes and dependency compatibility before writing, stages replacements, preserves the current six files beside the baseline and restores them if replacement fails. It leaves project sources and user settings untouched.

To inspect/build the old sources separately, preserving current edits:

```sh
git worktree add --detach ../Oryn_before_workshop 203b03f97193cec3a4bd9f8f98f06c0ff49aee33
```

The backup is local to this machine; the pushed commit is the durable source reference. Reinstall from that checkout if the local installation has since changed dependencies.

## Verification

Use the existing application tests and SDL smoke runner. The runner also captures the workshop overview/compact view, checks inspector UUID routing, confirms centered card dragging stays stationary on release and checks spinner animation without altering conversation state. Native Linux checks supplement the dummy driver; other platforms still need execution checks.
