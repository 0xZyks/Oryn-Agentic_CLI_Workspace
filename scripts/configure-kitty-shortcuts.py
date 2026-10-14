#!/usr/bin/env python3
"""Install narrowly scoped Kitty shortcut exceptions, with a backup and atomic write."""
import argparse
import os
from pathlib import Path
import shutil
import tempfile

BLOCK = """# BEGIN_ORYN_SHORTCUTS
# Oryn sets this user variable only during its terminal session.
# Keep Kitty's normal tab shortcuts outside Oryn.
map --when-focus-on var:oryn ctrl+tab
map --when-focus-on var:oryn ctrl+shift+tab
# END_ORYN_SHORTCUTS
"""


def configure(path, dry_run=False):
    original = path.read_text()
    if BLOCK in original:
        print("Oryn Kitty shortcuts already configured.")
        return
    if "BEGIN_ORYN_SHORTCUTS" in original or "END_ORYN_SHORTCUTS" in original:
        raise ValueError("Existing Oryn block differs; inspect it before changing the configuration.")
    if dry_run:
        print(BLOCK, end="")
        return
    backup = path.with_name(path.name + ".oryn-shortcuts.bak")
    if not backup.exists():
        shutil.copy2(path, backup)
    modified = original + ("\n" if original.endswith("\n") else "\n\n") + BLOCK
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", dir=path.parent, prefix=".oryn-kitty-", delete=False) as output:
            temporary = Path(output.name)
            output.write(modified)
        os.chmod(temporary, path.stat().st_mode & 0o777)
        os.replace(temporary, path)
        temporary = None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    print(f"Configured: {path}\nBackup: {backup}\nReload Kitty configuration with Ctrl+Shift+F5.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path.home() / ".config/kitty/kitty.conf")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    configure(args.config.expanduser().resolve(), args.dry_run)
