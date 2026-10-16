"""Measure a real local llama-server and verify streaming, disconnect and errors.

Owns a temporary server process; always stops it. Sends synthetic prompts only.
"""
import argparse
import http.client
import json
from pathlib import Path
import platform
import re
import subprocess
import sys
import time

from setup import ROOT, LOCAL, MANIFEST


def request(port, method, path, body=None):
    connection = http.client.HTTPConnection("127.0.0.1", port, timeout=30)
    try:
        connection.request(method, path, json.dumps(body) if body is not None else None,
                           {"Content-Type": "application/json"})
        response = connection.getresponse()
        return response.status, response.read().decode()
    finally:
        connection.close()


def gpu():
    result = subprocess.run(["nvidia-smi", "--query-gpu=name,memory.total,memory.used,driver_version",
                             "--format=csv,noheader,nounits"], text=True, capture_output=True, timeout=10)
    return result.stdout.strip() if result.returncode == 0 else "unavailable"


def host_memory(pid):
    """Linux process RSS/HWM in KiB; mmap file pages are not all private RAM."""
    values = {}
    for line in Path(f"/proc/{pid}/status").read_text().splitlines():
        key, _, value = line.partition(":")
        if key in ("VmRSS", "VmHWM", "RssAnon", "RssFile", "VmSwap"):
            values[key + "_kib"] = int(value.split()[0])
    for line in Path("/proc/meminfo").read_text().splitlines():
        if line.startswith("MemAvailable:"):
            values["system_available_kib"] = int(line.split()[1])
    return values


def chat(port, prompt, limit=160, cancel_after=None, thinking=False, sampling=None, system=None,
         require_progressive=True, timeout=60):
    connection = http.client.HTTPConnection("127.0.0.1", port, timeout=timeout)
    start = time.perf_counter()
    data = {"model": "oryn-core", "messages": [{"role": "user", "content": prompt}],
            "stream": True, "stream_options": {"include_usage": True}, "max_tokens": limit,
            "seed": 42, "cache_prompt": False, "chat_template_kwargs": {"enable_thinking": thinking}}
    if sampling:
        data.update(sampling)
    if system:
        data["messages"].insert(0, {"role": "system", "content": system})
    first = None
    first_event = None
    reasoning_chunks = 0
    reasoning_chars = 0
    chunks = 0
    content = ""
    usage = None
    timings = None
    finished = False
    finish_reason = None
    saw_done = False
    cancelled = False
    try:
        connection.request("POST", "/v1/chat/completions", json.dumps(data), {"Content-Type": "application/json"})
        response = connection.getresponse()
        if response.status != 200:
            raise RuntimeError(f"Chat HTTP {response.status}: {response.read().decode()[:500]}")
        while line := response.readline():
            if not line.startswith(b"data: "):
                continue
            payload = line[6:].strip()
            if payload == b"[DONE]":
                saw_done = True
                break
            event = json.loads(payload)
            usage = event.get("usage") or usage
            timings = event.get("timings") or timings
            for choice in event.get("choices", []):
                finished |= choice.get("finish_reason") is not None
                finish_reason = choice.get("finish_reason") or finish_reason
                text = choice.get("delta", {}).get("content") or ""
                reasoning = choice.get("delta", {}).get("reasoning_content") or ""
                if text or reasoning:
                    first_event = first_event if first_event is not None else time.perf_counter() - start
                if reasoning:
                    reasoning_chunks += 1
                    reasoning_chars += len(reasoning)
                if text:
                    first = first if first is not None else time.perf_counter() - start
                    chunks += 1
                    content += text
            if cancel_after and chunks >= cancel_after:
                cancelled = True
                break
    finally:
        # Close response AND connection: disposing only HTTPConnection can leave
        # the response's socket file alive, so it would not test a disconnect.
        if "response" in locals():
            response.close()
        connection.close()
    if require_progressive and (not content or chunks < 2):
        raise RuntimeError("No progressive content observed")
    if not cancelled and not (finished and saw_done):
        raise RuntimeError("Incomplete stream without a finish reason and [DONE]")
    if cancel_after and not cancelled:
        raise RuntimeError("Generation finished before the disconnect scenario")
    return {"prompt": prompt, "first_content_seconds": first,
            "first_event_seconds": first_event, "reasoning_chunks": reasoning_chunks,
            "reasoning_characters": reasoning_chars,
            "elapsed_seconds": time.perf_counter() - start, "content_chunks": chunks,
            "content": content, "usage": usage, "timings": timings,
            "client_disconnected": cancelled, "stream_complete": finished and saw_done,
            "finish_reason": finish_reason, "output_truncated": finish_reason == "length"}


def wait_idle(port, timeout=10):
    start = time.perf_counter()
    while time.perf_counter() - start < timeout:
        status, body = request(port, "GET", "/slots")
        if status == 200 and all(not slot["is_processing"] for slot in json.loads(body)):
            return time.perf_counter() - start
        time.sleep(0.1)
    raise RuntimeError("Server still processing after client disconnect")


def long_input_check(port, context, pid, thinking=False, limit=96, timeout=60):
    # Size a synthetic input using this model's tokenizer. This is a real
    # prefill workload, not just an empty reservation of a larger KV cache.
    target = min(context - 1024, int(context * 0.68))
    def document(rows):
        entries = [f"Entrée {i:04d} : module local, version stable, statut observé, source contrôlée."
                   for i in range(rows)]
        entries.insert(rows // 2, "La clé de validation du projet est LUMEN-742.")
        return ("Lis ces données de test. Retrouve la clé de validation du projet. "
                "Réponds seulement par une courte phrase contenant cette clé.\n" + "\n".join(entries))
    low, high = 1, context
    while low < high:
        middle = (low + high + 1) // 2
        status, body = request(port, "POST", "/tokenize", {"content": document(middle)})
        if status != 200:
            raise RuntimeError("Cannot measure synthetic prompt tokens")
        if len(json.loads(body)["tokens"]) <= target:
            low = middle
        else:
            high = middle - 1
    long_input = chat(port, document(low), limit=limit, thinking=thinking, timeout=timeout)
    long_input["retrieval_correct"] = "LUMEN-742" in long_input["content"]
    long_input["gpu_after"] = gpu()
    long_input["host_memory_after"] = host_memory(pid)
    return long_input


def extended_checks(port, context, results, pid, supports_reasoning=True):
    results["long_input"] = long_input_check(port, context, pid)
    reasoning = chat(port, "Un atelier fabrique 120 pièces. Il rejette 15 % des pièces, puis expédie les deux tiers des pièces restantes. Combien en garde-t-il ? Vérifie le calcul et termine par RESULTAT=nombre.",
                     limit=768, thinking=supports_reasoning)
    reasoning["expected_result"] = 34
    reasoning["answer_correct"] = bool(re.search(r"RESULTAT\s*=\s*34\b", reasoning["content"]))
    reasoning["gpu_after"] = gpu()
    reasoning["host_memory_after"] = host_memory(pid)
    results["bounded_reasoning"] = reasoning
    reasoning["separate_reasoning_supported"] = supports_reasoning
    if supports_reasoning and reasoning["reasoning_chunks"] == 0:
        raise RuntimeError("Reasoning mode requested, but no separate reasoning observed")
    # Exact-format toy probes have known answers; their limited scope is explicit.
    # Preserve responses for human review rather than infer broad coding quality.
    probes = [
        ("interface I { void M() { } } class C : I { }\nEn C# moderne, A: new C().M(); B: ((I)new C()).M(); quelles expressions compilent ? Réponds strictement A=OUI|NON;B=OUI|NON en choisissant OUI ou NON pour chacune.", "A=NON;B=OUI"),
        ("Deux générations asynchrones A puis B mettent à jour une même réponse UI. Après annulation de A, son provider émet encore un fragment tardif. En quatre phrases maximum, propose une protection et explique pourquoi le CancellationToken seul ne suffit pas.", None),
    ]
    quality = results["quality_probes"] = []
    for prompt, expected in probes:
        result = chat(port, prompt, limit=256)
        result["expected"] = expected
        if expected is not None:
            result["exact_format_correct"] = expected in re.sub(r"\s+", "", result["content"])
        quality.append(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/m1/baseline.json")
    parser.add_argument("--port", type=int, default=18080)
    parser.add_argument("--flash", choices=("on", "off", "auto"), default="off")
    parser.add_argument("--cache-type", choices=("f16", "q8_0"), default="f16")
    parser.add_argument("--context", type=int, default=4096)
    parser.add_argument("--model", type=Path, default=LOCAL / "models" / MANIFEST["model"]["filename"])
    parser.add_argument("--extended", action="store_true", help="Also test long input, bounded reasoning and quality probes")
    args = parser.parse_args()
    # Never direct the smoke to an existing server accidentally.
    try:
        request(args.port, "GET", "/health")
    except OSError:
        pass
    else:
        parser.error("Port already serves HTTP; choose another --port")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    known_models = [MANIFEST["model"], *MANIFEST["candidates"].values()]
    selected = next((model for model in known_models if model["filename"] == args.model.name), None)
    report = {"manifest": MANIFEST, "selected_model": selected or {"filename": str(args.model.resolve())},
              "platform": platform.platform(),
              "timestamp": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
              "settings": {"context": args.context, "parallel": 1, "flash": args.flash,
                           "cache_type": args.cache_type, "thinking": False,
                           "host_prompt_cache_mib": 0,
                           "sampling": (selected or {}).get("sampling", {"temperature": 0.7, "top_p": 0.8, "top_k": 20, "presence_penalty": 1.5}),
                           "extended_reasoning_budget": 256 if args.extended and
                               (selected or {}).get("supports_reasoning", True) else None}, "gpu_before": gpu()}
    log_path = args.output.with_suffix(".server.log")
    process = None
    try:
        with log_path.open("w") as log:
            start = time.perf_counter()
            process = subprocess.Popen([sys.executable, str(Path(__file__).with_name("run.py")),
                                        "--port", str(args.port), "--context", str(args.context),
                                        "--flash", args.flash, "--cache-type", args.cache_type,
                                        "--model", str(args.model.resolve())],
                                       stdout=log, stderr=subprocess.STDOUT)
            while time.perf_counter() - start < 180:
                if process.poll() is not None:
                    raise RuntimeError(f"Server exited {process.returncode}; see {log_path}")
                try:
                    if request(args.port, "GET", "/health")[0] == 200:
                        break
                except OSError:
                    pass
                time.sleep(0.2)
            else:
                raise RuntimeError("Server not ready within 180 seconds")
            report["startup_to_ready_seconds"] = time.perf_counter() - start
            report["gpu_loaded"] = gpu()
            report["host_memory_loaded"] = host_memory(process.pid)
            startup_log = log_path.read_text(errors="replace")
            report["full_gpu_offload"] = bool(re.search(r"offloaded (\d+)/\1 layers to GPU", startup_log))
            if not report["full_gpu_offload"]:
                raise RuntimeError("Full GPU layer offload not confirmed in server log")
            report["first_request"] = chat(args.port, "Explique en français la différence entre une interface et une classe abstraite en C#, avec un exemple bref.")
            report["warm_request"] = chat(args.port, "Dans une interface graphique C#, pourquoi éviter une inférence longue sur le thread de rendu ? Donne trois raisons.")
            report["cancel_request"] = chat(args.port, "Écris une longue liste numérotée de 100 conseils pour apprendre C#.", limit=2048, cancel_after=12)
            report["disconnect_to_idle_seconds"] = wait_idle(args.port)
            # Confirm compute stays stopped, not just a transient idle report.
            before = request(args.port, "GET", "/metrics")[1]
            time.sleep(0.5)
            after = request(args.port, "GET", "/metrics")[1]
            pattern = r"^llamacpp:tokens_predicted_total\s+(.+)$"
            a, b = re.search(pattern, before, re.M), re.search(pattern, after, re.M)
            if a is None or b is None or a[1] != b[1]:
                raise RuntimeError("Generation counter did not stabilize after disconnect")
            report["cancel_counter_stable"] = True
            report["after_cancel"] = chat(args.port, "Combien font 17 + 25 ? Réponds par une phrase.", limit=64)
            status, body = request(args.port, "POST", "/v1/chat/completions", {"messages": "invalid"})
            if not 400 <= status < 500:
                raise RuntimeError(f"Malformed request returned {status}")
            report["malformed_request_status"] = status
            if args.extended:
                report["extended"] = {}
                extended_checks(args.port, args.context, report["extended"], process.pid,
                                (selected or {}).get("supports_reasoning", True))
            report["gpu_after_requests"] = gpu()
            report["host_memory_after_requests"] = host_memory(process.pid)
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        if process is not None:
            shutdown = time.perf_counter()
            process.terminate()
            try:
                process.wait(timeout=15)
                report["forced_shutdown"] = False
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
                report["forced_shutdown"] = True
            report["shutdown_seconds"] = time.perf_counter() - shutdown
            report["server_exit_code"] = process.returncode
        report["gpu_after_shutdown"] = gpu()
        try:
            request(args.port, "GET", "/health")
            report["unavailable_after_shutdown"] = False
        except OSError:
            report["unavailable_after_shutdown"] = True
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
        print(f"Report: {args.output}\nServer log: {log_path}", flush=True)
    if report["forced_shutdown"] or not report["unavailable_after_shutdown"]:
        raise RuntimeError("Server shutdown did not complete cleanly")
    print("PASS: progressive content, disconnect stops compute, next request, invalid request, shutdown")


if __name__ == "__main__":
    main()
