"""Launch the standalone local M1 PoC; not yet connected to Oryn's UI."""
import argparse
import fcntl
import os
from pathlib import Path

from setup import LOCAL, MANIFEST


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--context", type=int, default=4096)
    parser.add_argument("--port", type=int, default=18080)
    parser.add_argument("--alias", default="oryn-core")
    parser.add_argument("--flash", choices=("on", "off", "auto"), default="off")
    parser.add_argument("--cache-type", choices=("f16", "q8_0"), default="f16")
    parser.add_argument("--reasoning", choices=("on", "off"), default=None)
    parser.add_argument("--reasoning-budget", type=int, default=None)
    parser.add_argument("--reasoning-effort", choices=("default", "low", "medium", "high"), default="default")
    parser.add_argument("--gpu-layers", type=int, default=99)
    parser.add_argument("--cpu-moe", type=int, default=0)
    parser.add_argument("--cpu-ffn", type=int, default=0)
    parser.add_argument("--moe-cache-mib", type=int, default=0)
    parser.add_argument("--batch", type=int, default=512)
    parser.add_argument("--ubatch", type=int, default=128)
    parser.add_argument("--model", type=Path, default=LOCAL / "models" / MANIFEST["model"]["filename"])
    args = parser.parse_args()
    if not 512 <= args.context <= 32768 or not 1024 <= args.port <= 65535:
        parser.error("Context must be 512..32768 and port 1024..65535")
    if min(args.cpu_moe, args.cpu_ffn, args.gpu_layers, args.moe_cache_mib) < 0 or not 1 <= args.ubatch <= args.batch <= 2048:
        parser.error("Invalid offload or batch sizes")
    backend = LOCAL / ("llama-" + MANIFEST["backend"]["version"])
    binaries = list(backend.rglob("llama-server"))
    if len(binaries) != 1 or not args.model.is_file():
        parser.error("Missing/ambiguous backend or model; run setup.py first")
    lock = os.open(LOCAL / ".backend.lock", os.O_CREAT | os.O_RDWR, 0o600)
    try:
        fcntl.flock(lock, fcntl.LOCK_SH | fcntl.LOCK_NB)
    except BlockingIOError:
        parser.error("Backend setup is active; retry when it finishes")
    os.set_inheritable(lock, True)
    # CUDA runtime archive and server archive may have different root directories.
    library_dirs = sorted({str(p.parent) for p in backend.rglob("*.so*")})
    env = os.environ.copy()
    env["LD_LIBRARY_PATH"] = ":".join(library_dirs + ([env["LD_LIBRARY_PATH"]] if env.get("LD_LIBRARY_PATH") else []))
    models = [MANIFEST["model"], *MANIFEST["candidates"].values()]
    selected = next((model for model in models if model["filename"] == args.model.name), {})
    args.reasoning = args.reasoning or selected.get("reasoning_default", "off")
    if args.reasoning_budget is None:
        args.reasoning_budget = selected.get("reasoning_budget_default",256)
    if args.reasoning_budget < -1:
        parser.error("Reasoning budget must be -1 or nonnegative")
    if args.reasoning == "on" and selected.get("supports_reasoning") is False:
        parser.error("This candidate does not declare separate reasoning support")
    sampling = selected.get("sampling", {"temperature": 0.7, "top_p": 0.8, "top_k": 20, "presence_penalty": 1.5})
    command = [str(binaries[0]), "--model", str(args.model.resolve()),
               "--alias", args.alias, "--host", "127.0.0.1", "--port", str(args.port),
               "--ctx-size", str(args.context), "--parallel", "1", "--n-gpu-layers", str(args.gpu_layers),
               "--n-cpu-moe", str(args.cpu_moe), "--batch-size", str(args.batch), "--ubatch-size", str(args.ubatch),
               "--n-cpu-ffn", str(args.cpu_ffn),
               "--moe-cache-mib", str(args.moe_cache_mib),
               "--flash-attn", args.flash, "--cache-type-k", args.cache_type,
               "--cache-type-v", args.cache_type, "--jinja", "--metrics", "--no-ui",
               "--offline", "--reasoning", args.reasoning, "--reasoning-budget", str(args.reasoning_budget),
               "--reasoning-effort", args.reasoning_effort,
               "--fit", "off", "--verbosity", "4", "--cache-ram", "0",
               "--reasoning-format", "deepseek", "--temp", str(sampling["temperature"]),
               "--top-p", str(sampling["top_p"]), "--top-k", str(sampling["top_k"]),
               "--min-p", "0", "--presence-penalty", str(sampling["presence_penalty"]),
               "--repeat-penalty", str(sampling.get("repeat_penalty",1.0)) ]
    print("Local PoC: http://127.0.0.1:%d ; Ctrl+C to stop" % args.port, flush=True)
    os.execve(command[0], command, env)


if __name__ == "__main__":
    main()
