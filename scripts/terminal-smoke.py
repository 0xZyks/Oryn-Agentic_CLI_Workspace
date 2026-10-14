#!/usr/bin/env python3
"""POSIX terminal smoke test using a real PTY; no third-party Python dependencies."""
import fcntl
import os
from pathlib import Path
import pty
import re
import select
import signal
import struct
import subprocess
import sys
import tempfile
import termios
import time


def run(executable, ascii_mode=False):
    with tempfile.TemporaryDirectory(prefix="oryn-terminal-") as project:
        Path(project, "folder").mkdir()
        Path(project, "folder", "nested.txt").write_text("nested preview")
        Path(project, "hello.txt").write_text("hello Oryn\nread-only preview")
        master, slave = pty.openpty()
        before = termios.tcgetattr(master)
        fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 120, 0, 0))
        transcript = bytearray()

        def attach_terminal():
            os.setsid()
            fcntl.ioctl(0, termios.TIOCSCTTY, 0)

        environment = dict(os.environ, TERM="xterm-256color", COLORTERM="truecolor", ORYN_DEBUG="1")
        environment.pop("NO_COLOR", None)
        # This synthetic terminal has no compositor window of its own.
        environment.pop("NIRI_SOCKET", None)
        args = [executable, "--keybindings", str(Path(project, "keys.json"))] + (["--ascii", "--no-color"] if ascii_mode else [])
        process = subprocess.Popen(args, cwd=project, stdin=slave, stdout=slave, stderr=slave,
                                   env=environment, preexec_fn=attach_terminal)

        def pump(seconds=0.5):
            deadline = time.monotonic() + seconds
            chunk = bytearray()
            while time.monotonic() < deadline:
                if select.select([master], [], [], 0.05)[0]:
                    try:
                        data = os.read(master, 65536)
                    except OSError:
                        break
                    chunk.extend(data)
                    # Answer terminal feature queries just as a basic emulator would.
                    if b"\x1b[?u" in data:
                        os.write(master, b"\x1b[?0u")
                    if b"\x1b[0c" in data:
                        os.write(master, b"\x1b[?1;2c")
                    if b"\x1b[6n" in data:
                        os.write(master, b"\x1b[1;1R")
            transcript.extend(chunk)
            return chunk.decode("utf-8", errors="replace")

        def send(keys, seconds=0.5):
            os.write(master, keys)
            return pump(seconds)

        def capture_frame(name):
            capture = os.environ.get("ORYN_SMOKE_CAPTURE_DIR")
            if capture:
                destination = Path(capture)
                destination.mkdir(parents=True, exist_ok=True)
                prefix = "mono" if ascii_mode else "color"
                (destination / f"{prefix}-{name}.ansi").write_text(transcript.decode("utf-8", errors="replace"))

        try:
            initial = ""
            startup_deadline = time.monotonic() + 10
            while time.monotonic() < startup_deadline and process.poll() is None:
                initial += pump(0.2)
                if all(title in initial for title in ["Files", "Chat", "Visualizer"]) and ("* Chat" if ascii_mode else "● Chat") in initial:
                    break
            assert all(title in initial for title in ["Files", "Chat", "Visualizer"]), "Wide layout missing panels"
            assert ("* Chat" if ascii_mode else "● Chat") in initial, "Active Chat tab missing"
            assert "Project map" not in initial, "Visualizer dock should be absent at startup"
            assert "Dock right" not in initial, "Visualizer should start as a tab"
            if ascii_mode:
                assert not any("\u2500" <= char <= "\u257f" for char in initial), "ASCII mode used Unicode borders"
                assert "=" * 24 in initial, "Full-width ASCII explorer accent missing"
            else:
                assert "▁" * 24 in initial, "Full-width explorer accent missing"
                assert "▔" not in initial, "Accent is still anchored to the top of its row"
            if not ascii_mode:
                assert not any(corner in initial for corner in "╭╮╰╯"), "Removed composer border reappeared"
            if not ascii_mode:
                assert "38;2;" in initial or "48;2;" in initial, "True-color surface palette missing"
                assert "48;2;36;38;48" in initial, "Focused explorer background missing"
                assert "48;2;19;21;27" in initial, "Inactive center background missing"
            capture = os.environ.get("ORYN_SMOKE_CAPTURE_DIR")
            if capture:
                destination = Path(capture)
                destination.mkdir(parents=True, exist_ok=True)
                (destination / ("mono.ansi" if ascii_mode else "color.ansi")).write_text(initial)
            center_focus = send(b"\x1bOQ")  # F2: Chat.
            assert ("=" if ascii_mode else "▁") * 90 in center_focus, "Accent did not cover the center's full width"
            if not ascii_mode:
                assert "48;2;32;35;44" in center_focus, "Center did not brighten on focus"
                assert "48;2;23;25;32" in center_focus, "Explorer did not dim after losing focus"
                assert "38;2;191;192;207" in center_focus, "Focused center text did not brighten"
            capture_frame("focus-chat")
            files_focus = send(b"\x1bOP")  # F1: Files.
            assert ("=" if ascii_mode else "▁") * 24 in files_focus, "Accent did not return to explorer"
            if not ascii_mode:
                assert "38;2;133;132;148" in files_focus, "Inactive center text did not dim"
            send(b"\x1b[<0;5;7M\x1b[<0;5;7m")  # Focus/select the first explorer row.
            send(b"\r")
            assert b"nested.txt" in transcript, "Folder did not expand"
            send(b"\x1b[D")
            send(b"\x1b[B")
            preview = send(b"\r")
            assert "hello Oryn" in preview, "File preview did not open"
            if not ascii_mode:
                assert "48;2;21;24;32" in preview, "Dedicated code canvas surface is missing"
            capture_frame("code-canvas")
            chat_tab = send(b"\x1b[<0;33;6M\x1b[<0;33;6m")
            assert "Chat tabs" in chat_tab, "Chat tab click did not focus its header"
            code_tab = send(b"\x1b[<0;44;6M\x1b[<0;44;6m")
            assert "hello Oryn" in code_tab, "Code tab click did not restore the preview"
            visualizer_tab = send(b"\x1b[<0;53;6M\x1b[<0;53;6m")
            assert "Central tab" in visualizer_tab, "Visualizer did not move into its own tab"
            assert ("* Visualizer" if ascii_mode else "● Visualizer") in visualizer_tab, "Visualizer tab not active"
            capture_frame("visualizer-tab")
            fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 18, 60, 0, 0))
            process.send_signal(signal.SIGWINCH)
            small_visualizer_tab = pump(0.7)
            assert "Program" in small_visualizer_tab, "Tabbed visualizer lost its demo graph in a small terminal"
            assert ("=" if ascii_mode else "▁") * 56 in small_visualizer_tab, "Compact accent width did not follow the panel"
            fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 120, 0, 0))
            process.send_signal(signal.SIGWINCH)
            pump(0.7)
            chat_after_map = send(b"\x1b[<0;33;6M\x1b[<0;33;6m")
            assert "Chat tabs" in chat_after_map, "Chat could not reopen with visualizer undocked"
            docked = send(b"\x04")
            assert "Dock right" in docked, "Ctrl+D did not restore the previous dock"
            dock_left = send(b"\x1b[1;6D")
            assert "Dock left" in dock_left, "Left dock shortcut failed"
            capture_frame("dock-left")
            dock_right = send(b"\x1b[1;6C")
            assert "Dock right" in dock_right, "Right dock shortcut failed"
            assert ("=" if ascii_mode else "▁") * 30 in dock_right, "Docked visualizer accent width is wrong"
            # Drag each separator, releasing away from its original hit area.
            send(b"\x1b[<0;27;14M")
            send(b"\x1b[<32;33;14M")
            resized_files = send(b"\x1b[<0;33;14m")
            assert "Files 30" in resized_files or b"Files 30" in transcript[-6000:], "Explorer separator did not resize"
            send(b"\x1b[<0;87;14M")
            map_drag = send(b"\x1b[<32;81;14M")
            resized_map = send(b"\x1b[<0;81;14m")
            assert "Visualizer 36" in resized_map or b"Visualizer 36" in transcript[-6000:], "Visualizer separator did not resize"
            assert ("=" if ascii_mode else "▁") * 36 in map_drag + resized_map, "Accent did not resize with its panel"
            capture_frame("resized-map")
            send(b"\x1bOS")  # F4: focus the currently docked visualizer.
            narrower = send(b"\x1b[1;5D")
            assert "Visualizer 34" in narrower, "Keyboard resize failed"
            send(b"\x1b[1;5C")
            send(b"\x1b[<0;33;14M")
            send(b"\x1b[<32;37;14M")
            cancelled = send(b"\x1b")
            assert "Resize cancelled" in cancelled, "Escape did not cancel the drag"
            send(b"\x1b[<0;37;14m")
            reset = send(b"\x1b[19~")  # F8.
            assert "Default panel sizes restored" in reset, "Size reset failed"
            send(b"\x1bOR")  # F3: center/code before the next Tab.
            changed = send(b"\x1b[9;5u")
            assert "Visualizer" in changed, "Tab did not change application focus"
            fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 18, 60, 0, 0))
            process.send_signal(signal.SIGWINCH)
            narrow = pump(0.7)
            assert "Project map" in narrow, "Narrow layout did not keep the focused panel"
            send(b"\x1b[9;5u")
            fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 120, 0, 0))
            process.send_signal(signal.SIGWINCH)
            pump(0.7)
            # The accent has its own row even in a short terminal.
            send(b"\x1bOP")
            fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 6, 30, 0, 0))
            process.send_signal(signal.SIGWINCH)
            tiny = pump(0.7)
            assert ("* Files" if ascii_mode else "● Files") in tiny, "Tiny terminal lost its focus heading"
            assert ("=" if ascii_mode else "▁") * 26 in tiny, "Tiny terminal lost its accent row"
            assert "╭" not in tiny, "Removed outline reappeared in tiny mode"
            fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 120, 0, 0))
            process.send_signal(signal.SIGWINCH)
            restored = pump(0.7)
            assert ("=" if ascii_mode else "▁") * 24 in restored, "Accent did not return after restoring terminal size"
            # Open and close a real menu, then quit through Oryn's global command.
            menu = send(b"\x1b[21~")
            assert "Project root" in menu, "Configured F10 menu shortcut did not open the menu"
            send(b"\x1b")
            send(b"\x11")
            process.wait(timeout=5)
            pump(0.1)
            assert process.returncode == 0, f"TUI exit code: {process.returncode}"
            assert b"\x1b[?1049l" in transcript, "Alternate screen was not restored"
            after = termios.tcgetattr(master)
            changed_modes = [i for i, (old, new) in enumerate(zip(before, after)) if old != new]
            assert not changed_modes, f"Terminal input modes were not restored (fields {changed_modes}; before={before[:6]}, after={after[:6]})"
            print("PASS: " + ("ASCII/mono" if ascii_mode else "Unicode/color") +
                  " PTY focus accent/backgrounds, startup, mouse, preview, visualizer tab/docks, separator drag/cancel, keyboard resize/reset, compact/tiny terminal, menu, exit and restoration.")
        except Exception:
            text = transcript.decode("utf-8", errors="replace")
            text = re.sub(r"\x1b\[[0-9;?]*[ -/]*[@-~]", "", text)
            print(text[-6000:], file=sys.stderr)
            raise
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            os.close(master)
            os.close(slave)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python3 scripts/terminal-smoke.py /path/to/oryn")
    binary = str(Path(sys.argv[1]).resolve())
    run(binary)
    run(binary, ascii_mode=True)
