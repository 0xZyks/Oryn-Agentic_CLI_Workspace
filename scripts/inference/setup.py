"""Download/verify the pinned Linux CUDA PoC into Git-ignored .local only."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import tarfile

ROOT = Path(__file__).resolve().parents[2]
LOCAL = ROOT / ".local/inference"
MANIFEST = json.loads(Path(__file__).with_name("manifest.json").read_text())


def verify(path, expected):
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if digest != expected:
        raise RuntimeError(f"SHA256 mismatch: {path}; move the invalid file aside before retrying")
    print(f"SHA256 OK: {path.name}", flush=True)


def obtain(url, path, checksum, verify_only):
    if path.exists():
        with path.open("rb") as stream:
            valid = hashlib.file_digest(stream, "sha256").hexdigest() == checksum
        if valid:
            print(f"Already verified: {path.name}", flush=True)
            return
    if not verify_only:
        command = ["curl", "-fL", "--retry", "3", "-C", "-", url, "-o", str(path)]
        config = None
        if url.startswith("https://huggingface.co/"):
            hf_directory = Path(os.environ.get("HF_HOME", str(Path.home() / ".cache/huggingface")))
            token_path = Path(os.environ.get("HF_TOKEN_PATH", str(hf_directory / "token")))
            token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGING_FACE_HUB_TOKEN")
            if not token and token_path.is_file():
                token = token_path.read_text().strip()
            if token:
                if any(char in token for char in '\r\n"\\\x00'):
                    raise RuntimeError("Invalid local Hugging Face token format")
                # Credentials stay on stdin, never in argv, logs or the manifest.
                command += ["--config", "-"]
                config = "header = " + json.dumps("Authorization: Bearer " + token) + "\n"
        subprocess.run(command, input=config, text=True, check=True)
    verify(path, checksum)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-only", action="store_true", help="No network; verify and extract existing files")
    parser.add_argument("--model", choices=("8b-q4", *MANIFEST["candidates"]), default="8b-q4")
    args = parser.parse_args()
    if platform.system() != "Linux" or platform.machine() != "x86_64":
        parser.error("This pinned binary is only for Linux x86_64; other platforms are not validated")
    downloads = LOCAL / "downloads"
    models = LOCAL / "models"
    backend = LOCAL / ("llama-" + MANIFEST["backend"]["version"])
    for directory in (downloads, models, backend):
        directory.mkdir(parents=True, exist_ok=True)
    # Keep this descriptor alive until setup finishes. run.py retains a shared
    # lock across exec so we never truncate a mapped library in a live server.
    lock = (LOCAL / ".backend.lock").open("a")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        parser.error("An inference server uses this installation; stop it before setup/verification")
    for item in MANIFEST["backend"]["archives"]:
        archive = downloads / item["name"]
        url = f"https://github.com/ggml-org/llama.cpp/releases/download/{MANIFEST['backend']['version']}/{item['name']}"
        obtain(url, archive, item["sha256"], args.verify_only)
        with tarfile.open(archive) as bundle:
            bundle.extractall(backend, filter="data")
    model = MANIFEST["model"] if args.model == "8b-q4" else MANIFEST["candidates"][args.model]
    url = f"https://huggingface.co/{model['repository']}/resolve/{model['revision']}/{model['filename']}"
    obtain(url, models / model["filename"], model["sha256"], args.verify_only)
    print(f"Ready in {LOCAL}. No service installed; launch with scripts/inference/run.py.")


if __name__ == "__main__":
    main()
