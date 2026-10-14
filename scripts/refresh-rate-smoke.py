#!/usr/bin/env python3
"""Black-box startup/runtime cadence checks using a fake read-only Niri IPC and a POSIX PTY."""
import fcntl
import json
import os
from pathlib import Path
import pty
import re
import select
import socket
import struct
import subprocess
import sys
import tempfile
import termios
import threading
import time


def run(binary):
    with tempfile.TemporaryDirectory(prefix="oryn-refresh-") as directory:
        ipc_path = str(Path(directory, "niri.sock"))
        scene = {"workspace": 1, "available": True}
        stop = threading.Event()
        listener = socket.socket(socket.AF_UNIX)
        listener.bind(ipc_path)
        listener.listen(8)
        listener.settimeout(0.1)

        def serve():
            while not stop.is_set():
                try:
                    client, _ = listener.accept()
                except socket.timeout:
                    continue
                with client:
                    client.settimeout(1)
                    request = bytearray()
                    while b"\n" not in request:
                        data = client.recv(64)
                        if not data or len(request) > 128:
                            break
                        request.extend(data)
                    name = json.loads(request)
                    replies = {
                        "Windows": [{"id": 7, "pid": 123, "workspace_id": scene["workspace"],
                                     "is_focused": False}],
                        "Workspaces": [{"id": 1, "output": "DP-1"}, {"id": 2, "output": "HDMI-1"}],
                        "Outputs": {"DP-1": {"current_mode": 0, "modes": [{"refresh_rate": 143998}]},
                                    "HDMI-1": {"current_mode": 0, "modes": [{"refresh_rate": 60000}]}},
                    }
                    reply = {"Ok": {name: replies[name]}} if scene["available"] else {"Err": "unavailable"}
                    client.sendall((json.dumps(reply) + "\n").encode())

        thread = threading.Thread(target=serve, daemon=True)
        thread.start()
        env = dict(os.environ, NIRI_SOCKET=ipc_path, KITTY_PID="123", TERM="xterm-256color",
                   COLORTERM="truecolor")
        for key in ("SSH_CONNECTION", "SSH_TTY", "TMUX", "STY", "NO_COLOR"):
            env.pop(key, None)
        master, slave = pty.openpty()
        before = termios.tcgetattr(master)
        fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 140, 0, 0))

        def attach():
            os.setsid()
            fcntl.ioctl(0, termios.TIOCSCTTY, 0)

        process = None
        try:
            for flags, expected in [([], 144), (["--fps", "120"], 120)]:
                result = subprocess.run([binary, "--check", *flags], cwd=directory, env=env,
                                        capture_output=True, text=True, timeout=5, check=True)
                assert json.loads(result.stdout)["renderCadence"]["FramesPerSecond"] == expected
            process = subprocess.Popen([binary], cwd=directory, env=env, stdin=slave,
                                       stdout=slave, stderr=slave, preexec_fn=attach)

            def wait_text(expected, seconds=4):
                text = ""
                deadline = time.monotonic() + seconds
                while time.monotonic() < deadline:
                    assert process.poll() is None, "TUI exited unexpectedly"
                    if select.select([master], [], [], 0.05)[0]:
                        data = os.read(master, 65536)
                        text += data.decode("utf-8", errors="replace")
                        for query, response in [(b"\x1b[?u", b"\x1b[?0u"),
                                                (b"\x1b[0c", b"\x1b[?1;2c"),
                                                (b"\x1b[6n", b"\x1b[1;1R")]:
                            if query in data:
                                os.write(master, response)
                        if expected in text:
                            return
                visible = re.sub(r"\x1b\[[0-9;?]*[ -/]*[@-~]", "", text)
                raise AssertionError(f"Missing cadence indicator: {expected}\n{visible[-4000:]}")

            wait_text("144 Hz auto (DP-1)", 10)
            scene["workspace"] = 2
            wait_text("60 Hz auto (HDMI-1)")
            scene["workspace"] = 1
            wait_text("144 Hz auto (DP-1)")
            os.write(master, b"\x1bv")  # Open View using its mnemonic.
            wait_text("Refresh:")
            os.write(master, b"6")
            wait_text("60 Hz manual")
            scene["workspace"] = 2
            time.sleep(1.1)
            os.write(master, b"\x1bv")  # Alt+V opens View directly.
            wait_text("Refresh:")
            os.write(master, b"a")
            wait_text("60 Hz auto (HDMI-1)")
            scene["available"] = False
            wait_text("60 Hz fallback")
            scene["available"] = True
            scene["workspace"] = 1
            wait_text("144 Hz auto (DP-1)")
            os.write(master, b"\x11")
            process.wait(timeout=5)
            assert process.returncode == 0
            assert termios.tcgetattr(master) == before, "Terminal modes were not restored"
            print("PASS: startup 144 Hz, monitor moves 144/60, manual/auto menu, IPC failure/recovery, exit/restoration.")
        finally:
            if process is not None and process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            stop.set()
            thread.join(timeout=2)
            listener.close()
            os.close(master)
            os.close(slave)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python3 scripts/refresh-rate-smoke.py /path/to/oryn")
    run(str(Path(sys.argv[1]).resolve()))
