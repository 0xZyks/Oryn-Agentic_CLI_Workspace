#!/usr/bin/env python3
"""Validate or restore the six assemblies changed by the workshop prototype."""
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import uuid

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('backup', type=Path)
parser.add_argument('--destination', type=Path, default=Path.home() / '.local/share/oryn')
parser.add_argument('--apply', action='store_true')
args = parser.parse_args()
backup = args.backup.resolve()
destination = args.destination.resolve()
files = ('oryn.dll', 'oryn.pdb', 'Oryn.App.dll', 'Oryn.App.pdb', 'Oryn.Ui.Sdl.dll', 'Oryn.Ui.Sdl.pdb')
manifest = json.loads((backup / 'manifest.json').read_text())
source = backup / 'installed'
for name in files:
    expected = manifest['installed_hashes'][name]
    if hashlib.sha256((source / name).read_bytes()).hexdigest() != expected:
        raise SystemExit(f'Baseline hash mismatch: {name}')
    if not (destination / name).is_file() or (destination / name).is_symlink():
        raise SystemExit(f'Unexpected installed assembly: {name}')
old = json.loads((source / 'oryn.deps.json').read_text())
current = json.loads((destination / 'oryn.deps.json').read_text())
if old['runtimeTarget'] != current['runtimeTarget'] or old['libraries'] != current['libraries']:
    raise SystemExit('Installed dependencies changed; reinstall the baseline from its separate checkout.')
print('Validated baseline', manifest['baseline_commit'], 'for', destination)
if not args.apply:
    print('Preview only. Close Oryn and add --apply to restore the six assemblies.')
    raise SystemExit(0)
saved = backup / ('before-rollback-' + datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8])
saved.mkdir()
staged = {}
replaced = []
try:
    for name in files:
        shutil.copy2(destination / name, saved / name)
        stage = destination / ('.' + name + '.restore-' + uuid.uuid4().hex + '.tmp')
        staged[name] = stage
        shutil.copy2(source / name, stage)
    for name in files:
        os.replace(staged[name], destination / name)
        replaced.append(name)
    for name in files:
        if hashlib.sha256((destination / name).read_bytes()).hexdigest() != manifest['installed_hashes'][name]:
            raise RuntimeError(f'Restored assembly verification failed: {name}')
except BaseException:
    for name in replaced:
        stage = destination / ('.' + name + '.undo-' + uuid.uuid4().hex + '.tmp')
        shutil.copy2(saved / name, stage)
        os.replace(stage, destination / name)
    raise
finally:
    for stage in staged.values():
        if stage.exists():
            stage.unlink()
print('Restored baseline. Previous assemblies preserved at', saved)
