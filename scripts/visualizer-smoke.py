#!/usr/bin/env python3
"""Fictitious graph preview in a real POSIX PTY: selection, density, docking and resize."""
import fcntl
import importlib.util
import os
from pathlib import Path
import signal
import struct
import sys
import tempfile
import termios

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("keys", Path(__file__).with_name("keyboard-smoke.py"))
keys = importlib.util.module_from_spec(spec)
spec.loader.exec_module(keys)


def run(binary, ascii_mode=False):
    with tempfile.TemporaryDirectory(prefix="oryn-graph-") as root:
        profile = Path(root, "keys.json")
        profile.write_text("{}")
        session = keys.Session(binary, root, profile, ["--ascii", "--no-color"] if ascii_mode else [])
        def capture(name):
            destination = os.environ.get("ORYN_SMOKE_CAPTURE_DIR")
            if destination:
                Path(destination).mkdir(parents=True, exist_ok=True)
                Path(destination, ("mono-" if ascii_mode else "color-") + name + ".ansi").write_bytes(session.all_output)
        try:
            session.expect("Super=Ctrl")
            initial = session.send(b"\x1bOS", "Visualizer content")  # F4.
            assert "Program" in initial and "DEMO" in initial
            capture("standard")
            session.send(b"\t")
            session.send(b"\x1b[Z")  # Graph controls stay inside the visualizer.
            session.send(b"\x1b[<0;45;16M\x1b[<0;45;16m", "Graph: Program / Standard")
            session.send(b"\x1b[C", "Graph: WorkspaceState / Standard")
            session.send(b"\x1b[B", "Graph: ChatPreviewState / Standard")
            detailed = session.send(b"\x1b[5~", "Graph: ChatPreviewState / Details")
            assert "Send()" in detailed, "Detail preset lost selected node members"
            capture("details")
            session.send(b"\x1b[6~", "Graph: ChatPreviewState / Standard")
            session.send(b"\x1b[6~", "Graph: ChatPreviewState / Overview")
            capture("overview")
            session.send(b"+", "Graph: ChatPreviewState / Standard")
            session.send(b"+", "Graph: ChatPreviewState / Details")
            session.send(b"-", "Graph: ChatPreviewState / Standard")
            session.send(b"\x0b", "Keybindings")
            session.send(b"\x1bg", "Group: Graph")
            session.send(b"\x1bc")  # Cancel editor; keep graph state.
            session.send(b"\x1b[13;5u", "Visualizer tabs")
            session.send(b"\x1b[D", "Code tabs")  # Header arrows still choose tabs.
            session.send(b"\x1bOS", "Visualizer content")
            session.send(b"\x04", "Dock right")
            capture("dock")
            fcntl.ioctl(session.slave, termios.TIOCSWINSZ, struct.pack("HHHH", 18, 60, 0, 0))
            session.process.send_signal(signal.SIGWINCH)
            resized = session.pump(0.6)
            assert "ChatPreviewState" in resized, "Resize lost the selected node"
            session.send(b"\x1b[A", "Graph: WorkspaceState / Standard")
            session.send(b"\x1b[D", "Graph: Program / Standard")
            session.send(b"\x1b[B", "Graph: WorkspaceFiles / Standard")
            session.send(b"\x1b[B", "Graph: IWorkspaceFiles / Standard")
            capture("narrow")
            session.close()
            print("PASS: " + ("ASCII/mono" if ascii_mode else "Unicode/color") +
                  " demo graph, mouse/arrows, detail presets, +/- zoom, Graph settings, tab scope, docking, resize and restoration.")
        finally:
            session.cleanup()


if __name__ == "__main__":
    run(str(Path(sys.argv[1]).resolve()))
    run(str(Path(sys.argv[1]).resolve()), True)
