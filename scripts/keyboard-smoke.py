#!/usr/bin/env python3
"""POSIX PTY checks for scoped shortcuts, leader keys, and the grouped settings editor."""
import fcntl
import json
import os
from pathlib import Path
import pty
import re
import select
import struct
import subprocess
import sys
import tempfile
import termios
import time


class Session:
    def __init__(self, binary, directory, profile, options=()):
        self.master, self.slave = pty.openpty()
        self.before = termios.tcgetattr(self.master)
        fcntl.ioctl(self.slave, termios.TIOCSWINSZ, struct.pack("HHHH", 36, 160, 0, 0))
        env = dict(os.environ, TERM="xterm-256color", COLORTERM="truecolor", ORYN_DEBUG="1")
        env.pop("NIRI_SOCKET", None)
        env.pop("NO_COLOR", None)

        def attach():
            os.setsid()
            fcntl.ioctl(0, termios.TIOCSCTTY, 0)

        self.process = subprocess.Popen([binary, "--keybindings", str(profile), *options], cwd=directory,
                                        env=env, stdin=self.slave, stdout=self.slave, stderr=self.slave,
                                        preexec_fn=attach)
        self.all_output = bytearray()

    def pump(self, seconds=0.25):
        deadline = time.monotonic() + seconds
        result = bytearray()
        while time.monotonic() < deadline:
            if select.select([self.master], [], [], 0.02)[0]:
                data = os.read(self.master, 65536)
                result.extend(data)
                for query, response in [(b"\x1b[?u", b"\x1b[?0u"),
                                        (b"\x1b[0c", b"\x1b[?1;2c"),
                                        (b"\x1b[6n", b"\x1b[1;1R")]:
                    if query in data:
                        os.write(self.master, response)
        self.all_output.extend(result)
        return result.decode("utf-8", errors="replace")

    def expect(self, needle, initial="", timeout=5):
        text = initial
        deadline = time.monotonic() + timeout
        while needle not in text and time.monotonic() < deadline and self.process.poll() is None:
            text += self.pump(0.1)
        if needle not in text:
            text = self.all_output[-100000:].decode("utf-8", errors="replace")
            visible = re.sub(r"\x1b\[[0-9;?]*[ -/]*[@-~]", "", text)
            visible = re.sub(r" {3,}", " ", visible)
            raise AssertionError(f"Missing {needle!r}:\n{visible[-5000:]}")
        return text

    def send(self, keys, needle=None):
        os.write(self.master, keys)
        result = self.pump()
        return self.expect(needle, result) if needle else result

    def close(self, quit_keys=b"\x03"):
        try:
            self.send(quit_keys)
            self.process.wait(timeout=5)
            assert self.process.returncode == 0
            assert b"\x1b[?1049l" in self.all_output
            assert termios.tcgetattr(self.master) == self.before
            if b"\x1b]1337;SetUserVar=oryn=MQ==\x07" in self.all_output:
                assert b"\x1b]1337;SetUserVar=oryn\x07" in self.all_output, "Kitty shortcut scope was not cleared"
        finally:
            self.cleanup()

    def cleanup(self):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        if self.master is not None:
            os.close(self.master)
            os.close(self.slave)
            self.master = None


def run(binary):
    with tempfile.TemporaryDirectory(prefix="oryn-keyboard-") as directory:
        profile = Path(directory, "keys.json")
        for super_key, next_panel, toggle in [
            ("Ctrl", b"\x1b[9;5u", b"\x1b[13;5u"),
            ("Alt", b"\x1b[9;3u", b"\x1b[13;3u"),
            ("Space", b" \t", b" \r"),
        ]:
            profile.write_text(json.dumps({"Super": super_key}))
            session = Session(binary, directory, profile)
            try:
                session.expect("Super=" + super_key)
                session.send(b"\t")
                session.send(b"\x1b[Z")  # Bare Tab/Shift+Tab must stay in Files.
                session.send(next_panel, "Chat content")
                session.send(b"\t")
                session.send(b"\x1b[Z")  # Chat history -> composer stays local.
                # Printable leaders are ordinary text in the prompt field.
                session.send(b"\x1b[17~" if super_key == "Space" else toggle, "Chat tabs")
                session.send(b"\x1b[C", "Code tabs")
                session.send(b"\x1b[C", "Visualizer tabs")
                session.send(b"\x1b[D", "Code tabs")
                session.send(b"\r", "Code content")  # Enter confirms the tab.
                session.send(b"\t")
                session.send(b"\x1b[Z")  # No global/native escape from Code.
                session.send(b"\x1b[C")  # Content arrow must not activate the next tab.
                session.send(b"\x1b[17~", "Code tabs")  # F6: portable toggle fallback.
                session.send(b"\x1b", "Code content")  # Escape remains a way back.
                session.send(b"\x1b[17~", "Code tabs")
                session.send(b"\t", "Visualizer tabs")
                session.send(b"\x1b[Z", "Code tabs")
                session.send(b"\r", "Code content")
                session.close()
                print(f"PASS: {super_key} Super, panel traversal, scoped tab arrows, content/fallback focus, restoration.")
            finally:
                session.cleanup()

        profile.write_text("{}")
        session = Session(binary, directory, profile)
        try:
            session.expect("Super=Ctrl")
            session.send(b"\x0b", "Keybindings")  # Ctrl+K.
            session.send(b"\x01Alt")  # Select all Super field text, then type Alt.
            session.send(b"\x1ba", "Super=Alt")  # Apply to session.
            assert json.loads(profile.read_text()) == {}, "Apply must not save the profile"
            session.send(b"\x1b[9;3u", "Chat content")
            session.send(b"\x1bk", "Keybindings")  # Alt+K.
            session.send(b"\x1bp", "Group: Panels")
            session.send(b"\t")  # Action list -> bindings field after deferred group focus.
            session.send(b"\x01Super+J")
            session.send(b"\x1bs")  # Save and close; unchanged footer text need not be emitted again.
            saved = json.loads(profile.read_text())
            assert saved["Super"] == "Alt" and saved["Panels"]["NextPanel"] == ["Super+J"], saved
            session.send(b"\x1bj", "Files")  # Remapped traversal works immediately.
            session.send(b"\x1bk", "Keybindings")
            session.send(b"\x1bp", "Group: Panels")
            session.send(b"\x1br", "Super+Tab")  # Reset only this group.
            session.send(b"\x1bs")
            saved = json.loads(profile.read_text())
            assert saved["Panels"]["NextPanel"] == ["Super+Tab"]
            session.close()
        finally:
            session.cleanup()
        restarted = Session(binary, directory, profile)
        try:
            restarted.expect("Super=Alt")
            restarted.send(b"\x1b[21~", "Project root")
            restarted.send(b"\x1b")
            restarted.send(b"\x1b[9;3u", "Chat content")
            restarted.close()
        finally:
            restarted.cleanup()
        print("PASS: grouped editor, session Apply, persistent Save, remapping, group reset and restart recovery.")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python3 scripts/keyboard-smoke.py /path/to/oryn")
    run(str(Path(sys.argv[1]).resolve()))
