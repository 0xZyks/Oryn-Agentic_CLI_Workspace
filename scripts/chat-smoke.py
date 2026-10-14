#!/usr/bin/env python3
"""M0 chat preview: real terminal input, submit, scoped focus, resize and restoration."""
import fcntl
import importlib.util
import json
import os
from pathlib import Path
import signal
import struct
import sys
import tempfile
import termios

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("keyboard_smoke", Path(__file__).with_name("keyboard-smoke.py"))
keyboard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(keyboard)


def run(binary, ascii_mode=False):
    with tempfile.TemporaryDirectory(prefix="oryn-chat-") as directory:
        profile = Path(directory, "keys.json")
        profile.write_text(json.dumps({"Super": "Ctrl"}))
        session = keyboard.Session(binary, directory, profile, ["--ascii", "--no-color"] if ascii_mode else [])
        try:
            session.expect("Super=Ctrl")
            session.send(b"\x1bOQ", "Chat content")
            assert b"Send" not in session.all_output, "Removed Send control reappeared"
            blank = session.send(b"\r")
            assert "Hello World!" not in blank, "Empty input produced a reply"
            session.send(b"Say Hello World!")
            pending = session.send(b"\r", "Thinking...")
            assert "Demo reply received" not in pending, "Reply did not wait"
            capture = os.environ.get("ORYN_SMOKE_CAPTURE_DIR")
            if capture:
                Path(capture).mkdir(parents=True, exist_ok=True)
                Path(capture, "thinking-mono.ansi" if ascii_mode else "thinking-color.ansi").write_bytes(session.all_output)
            session.send(b"Next draft")
            sent = session.expect("Demo reply received")
            assert "Hello World!" in sent
            assert "User" in sent and "Oryn" in sent, "Missing message identities"
            session.send(b"\x01First draft line")  # Completion must keep the next draft editable.
            session.send(b"\x1b[13;2u")  # Shift+Enter with Kitty/CSI-u.
            # Both lines must still be in the same draft after switching tabs;
            # a redraw can legitimately re-emit an earlier Hello World reply.
            session.send(b"Second draft line")
            capture = os.environ.get("ORYN_SMOKE_CAPTURE_DIR")
            if capture:
                Path(capture).mkdir(parents=True, exist_ok=True)
                Path(capture, "draft-mono.ansi" if ascii_mode else "draft-color.ansi").write_bytes(session.all_output)
            session.send(b"\x1bOR", "Code content")
            restored = session.send(b"\x1bOQ", "Chat content")
            assert "First draft line" in restored and "Second draft line" in restored, "Changing tabs lost the multiline draft"
            multiline = session.send(b"\r", "Demo reply received")
            assert "First draft line" in multiline and "Second draft line" in multiline
            session.send(b"\t")  # Composer -> history.
            session.send(b"\t")  # History -> composer.
            session.send(b"\x01abc\x1b[DZ")
            session.send(b"\r", "Demo reply received")  # Left moves the input cursor, not a tab.
            assert b"abZc" in session.all_output
            session.send(b"Click to edit")
            session.send(b"\x1b[<0;50;31M\x1b[<0;50;31m")
            session.send(b"\x01Mouse focus")
            session.send(b"\r", "Demo reply received")
            capture = os.environ.get("ORYN_SMOKE_CAPTURE_DIR")
            if capture:
                Path(capture).mkdir(parents=True, exist_ok=True)
                Path(capture, "chat-mono.ansi" if ascii_mode else "chat-color.ansi").write_bytes(session.all_output)
            # Resize with the composer focused and send a wrapped prompt.
            fcntl.ioctl(session.slave, termios.TIOCSWINSZ, struct.pack("HHHH", 18, 60, 0, 0))
            session.process.send_signal(signal.SIGWINCH)
            session.pump(0.5)
            session.send(b"A long prompt with several words to wrap across the narrow chat.")
            session.send(b"\r", "Demo reply received")
            session.send(b"\x1b[13;5u", "Chat tabs")
            session.send(b"\x1b[C", "Code tabs")
            session.send(b"\x1b[D", "Chat tabs")
            session.send(b"\x1b[13;5u", "Chat content")
            for i in range(8):
                session.send(f"Message {i}".encode())
                session.send(b"\r", "Demo reply received")
            session.close()
            print("PASS: " + ("ASCII/mono" if ascii_mode else "Unicode/color") +
                  " multiline chat, Shift+Enter/Enter, draft, cursor, mouse focus, wrapping, scrolling and restoration.")
        finally:
            session.cleanup()

    # Space as Oryn leader must remain ordinary prompt text.
    if not ascii_mode:
        with tempfile.TemporaryDirectory(prefix="oryn-chat-space-") as directory:
            profile = Path(directory, "keys.json")
            profile.write_text('{"Super":"Space"}')
            session = keyboard.Session(binary, directory, profile)
            try:
                session.expect("Super=Space")
                session.send(b"\x1bOQ", "Chat content")
                session.send(b"Words with spaces")
                session.send(b"\r", "Demo reply received")
                assert b"Words with spaces" in session.all_output
                session.close()
                print("PASS: Space leader preserves prompt spaces.")
            finally:
                session.cleanup()


if __name__ == "__main__":
    run(str(Path(sys.argv[1]).resolve()))
    run(str(Path(sys.argv[1]).resolve()), True)
