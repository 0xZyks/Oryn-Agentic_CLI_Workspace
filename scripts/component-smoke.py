#!/usr/bin/env python3
"""M0 component/member navigation and isolated demo chats in a real POSIX PTY."""
import importlib.util
import json
import fcntl
import os
import signal
import struct
import termios
from pathlib import Path
import sys
import tempfile

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("keys", Path(__file__).with_name("keyboard-smoke.py"))
keys = importlib.util.module_from_spec(spec)
spec.loader.exec_module(keys)


def run(binary, super_key="Ctrl"):
    with tempfile.TemporaryDirectory(prefix="oryn-components-") as root:
        profile = Path(root, "keys.json")
        profile.write_text(json.dumps({"Super": super_key}))
        session = keys.Session(binary, root, profile)
        alt_enter = b"\x1b[13;3u"
        try:
            session.expect("Super=" + super_key)
            session.send(b"\x1bOS", "Visualizer content")
            session.send(alt_enter, "Options: Program")
            details = session.send(b"\x1bd", "Details: Program")
            session.expect("None (graph card)", details)
            session.send(b"\x1b")
            session.send(b"\r", "Graph: Program.Main(args) / Members")
            # Direct mouse selection of the second member must keep its own identity.
            session.send(b"\x1b[<0;45;20M\x1b[<0;45;20m", "Graph: Program.OpenWorkspace() / Members")
            session.send(b"\x1b[A", "Graph: Program.Main(args) / Members")
            session.send(b"\x1b[B", "Graph: Program.OpenWorkspace() / Members")
            session.send(alt_enter, "Options: OpenWorkspace()")
            details = session.send(b"\x1bd", "Details: OpenWorkspace()")
            details = session.expect("Parent: Program", details)
            session.expect("000000000102", details)
            session.send(b"\x1b")
            session.send(b"\r", "Details: OpenWorkspace()")
            session.send(b"\x1b")
            session.send(alt_enter, "Options: OpenWorkspace()")
            session.send(b"\x1bc", "Chat: OpenWorkspace()")
            session.send(b"component question\r", "Hello World!")
            session.send(b"retained draft")
            session.send(b"\x1b")
            session.send(alt_enter, "Options: OpenWorkspace()")
            reopened = session.send(b"\x1bc", "Chat: OpenWorkspace()")
            reopened = session.expect("retained draft", reopened)
            reopened = session.expect("component question", reopened)
            capture = os.environ.get("ORYN_SMOKE_CAPTURE_DIR")
            if capture:
                Path(capture).mkdir(parents=True, exist_ok=True)
                Path(capture, "component-chat-" + super_key + ".ansi").write_bytes(session.all_output)
            assert "retained draft" in reopened and "component question" in reopened
            session.send(b"\x1b")
            session.send(b"\x1b[A", "Graph: Program.Main(args) / Members")
            session.send(alt_enter, "Options: Main(args)")
            other = session.send(b"\x1bc", "Chat: Main(args)")
            other = session.expect("Write a prompt below", other)
            assert "retained draft" not in other and "component question" not in other
            session.send(b"\x1b")
            session.send(b"\x1b", "Graph: Program / Standard")
            session.send(alt_enter, "Options: Program")
            parent = session.send(b"\x1bc", "Chat: Program")
            parent = session.expect("Write a prompt below", parent)
            assert "component question" not in parent
            session.send(b"\x1b")
            fcntl.ioctl(session.slave, termios.TIOCSWINSZ, struct.pack("HHHH", 16, 45, 0, 0))
            session.process.send_signal(signal.SIGWINCH)
            session.pump(0.5)
            session.send(alt_enter, "Options: Program")
            session.send(b"\x1bc", "Chat: Program")
            session.send(b"narrow\r", "Hello World!")
            session.send(b"\x1b")
            session.send(b"\x1bOQ", "Chat content")
            global_chat = session.send(b"\t")
            assert "component question" not in global_chat
            session.close()
            print("PASS: Super=" + super_key + " card/member menus, details, Enter/Esc, per-UUID chats, retained drafts, global isolation and restoration.")
        finally:
            session.cleanup()


if __name__ == "__main__":
    run(str(Path(sys.argv[1]).resolve()))
    run(str(Path(sys.argv[1]).resolve()), "Alt")
