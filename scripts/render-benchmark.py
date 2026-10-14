#!/usr/bin/env python3
"""Measure terminal output bursts during a 120 Hz splitter drag on a POSIX PTY.

This measures application output, not frames presented by a terminal emulator.
Chunks separated by less than 4 ms count as one output burst.
"""
import fcntl
import json
import math
import os
from pathlib import Path
import pty
import select
import signal
import statistics
import struct
import subprocess
import sys
import tempfile
import termios
import time


def benchmark(executable):
    with tempfile.TemporaryDirectory(prefix="oryn-render-") as project:
        Path(project, "example.txt").write_text("render benchmark")
        master, slave = pty.openpty()
        before = termios.tcgetattr(master)
        fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 120, 0, 0))

        def attach():
            os.setsid()
            fcntl.ioctl(0, termios.TIOCSCTTY, 0)

        env = dict(os.environ, TERM="xterm-256color", COLORTERM="truecolor")
        env.pop("NO_COLOR", None)
        env.pop("NIRI_SOCKET", None)  # Synthetic PTY: benchmark the 60 Hz fallback.
        process = subprocess.Popen([executable], cwd=project, stdin=slave, stdout=slave,
                                   stderr=slave, env=env, preexec_fn=attach)

        def read(timeout):
            if not select.select([master], [], [], max(0, timeout))[0]:
                return b""
            data = os.read(master, 65536)
            for query, response in [(b"\x1b[?u", b"\x1b[?0u"),
                                    (b"\x1b[0c", b"\x1b[?1;2c"),
                                    (b"\x1b[6n", b"\x1b[1;1R")]:
                if query in data:
                    os.write(master, response)
            return data

        def drain(seconds):
            end = time.monotonic() + seconds
            total = 0
            while time.monotonic() < end:
                total += len(read(min(0.01, end - time.monotonic())))
            return total

        try:
            startup = bytearray()
            deadline = time.monotonic() + 10
            while b"Explore. Understand. Build." not in startup:
                if process.poll() is not None or time.monotonic() >= deadline:
                    raise RuntimeError("Workspace did not start")
                startup.extend(read(0.05))
            drain(0.5)
            idle_bytes = drain(1)
            # Default explorer: inset 2 + width 24 -> splitter at column 27.
            os.write(master, b"\x1b[<0;27;14M")
            drain(0.1)
            duration = 3
            start = time.monotonic()
            next_move = start
            bursts = []
            last_output = 0
            output_bytes = 0
            while time.monotonic() - start < duration:
                now = time.monotonic()
                if now >= next_move:
                    column = round(43 + 15 * math.sin((now - start) * math.tau))
                    os.write(master, f"\x1b[<32;{column};14M".encode())
                    next_move = now + 1 / 120
                data = read(min(0.001, next_move - time.monotonic()))
                if data:
                    now = time.monotonic()
                    if now - last_output >= 0.004:
                        bursts.append(now)
                    last_output = now
                    output_bytes += len(data)
            os.write(master, b"\x1b[<0;43;14m")
            drain(0.2)
            os.write(master, b"\x11")
            deadline = time.monotonic() + 5
            while process.poll() is None and time.monotonic() < deadline:
                read(0.01)
            if process.poll() != 0:
                raise RuntimeError("Workspace did not exit cleanly")
            if termios.tcgetattr(master) != before:
                raise RuntimeError("Terminal modes were not restored")
            gaps = sorted((b - a) * 1000 for a, b in zip(bursts, bursts[1:]))
            if not gaps:
                raise RuntimeError("No sustained rendering detected during drag")
            print(json.dumps({
                "duration_seconds": duration,
                "output_bursts_per_second": round(len(bursts) / duration, 1),
                "median_gap_ms": round(statistics.median(gaps), 2),
                "p95_gap_ms": round(gaps[math.ceil(len(gaps) * 0.95) - 1], 2),
                "active_output_bytes": output_bytes,
                "idle_output_bytes_per_second": idle_bytes,
            }, indent=2))
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
        raise SystemExit("Usage: python3 scripts/render-benchmark.py /path/to/oryn")
    benchmark(str(Path(sys.argv[1]).resolve()))
