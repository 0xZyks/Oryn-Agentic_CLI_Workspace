"""Small local comparison of direct answers and a sequential planner/coder pair.

Synthetic C# questions only. This is a PoC evaluation, not an agent runtime.
"""
import argparse
from contextlib import contextmanager
import json
from pathlib import Path
import re
import subprocess
import sys
import time

from setup import ROOT, LOCAL, MANIFEST
from smoke import chat, gpu, host_memory, request

SAMPLING = {"temperature": 0.2, "top_p": 0.95, "top_k": 40, "min_p": 0.0, "presence_penalty": 0.0}
TASKS = [
    {"id": "default-interface", "kind": "json", "expected": {"A": False, "B": True},
     "prompt": 'C# moderne : interface I { void M() { } } class C : I { }\nA = new C().M(); B = ((I)new C()).M(); Indique si chaque expression compile. Réponds seulement avec un objet JSON contenant les booléens A et B.'},
    {"id": "closure-for", "kind": "json", "expected": {"output": "333"},
     "prompt": 'C# : var a = new List<Action>(); for (int i=0; i<3; i++) a.Add(() => Console.Write(i)); foreach (var f in a) f(); Quel texte est affiché ? Réponds seulement en JSON : clé output, valeur chaîne.'},
    {"id": "deferred-linq", "kind": "json", "expected": {"result": 6},
     "prompt": 'C# : var xs = new List<int> { 1, 2 }; var q = xs.Where(x => x % 2 == 0); xs.Add(4); var r = q.Sum(); Quelle est la valeur de r ? Réponds seulement en JSON : clé result, valeur entière.'},
    {"id": "integer-division", "kind": "json", "expected": {"result": 2},
     "prompt": 'C# : int a=5, b=2; double r=a/b; Quelle est la valeur numérique de r ? Réponds seulement en JSON : clé result, valeur numérique.'},
    {"id": "exception-filter", "kind": "json", "expected": {"result": 1},
     "prompt": 'C# : int x=0; try { throw new Exception(); } catch(Exception) when (++x == 2) { } catch(Exception) { } Console.Write(x); Quel entier est affiché ? Réponds seulement en JSON : clé result, valeur entière.'},
    {"id": "cancellation-choice", "kind": "json", "expected": {"choice": 3},
     "prompt": 'Un provider ignore parfois CancellationToken. Une génération A annulée émet encore des fragments après le lancement de B. Choisis la protection correcte : 1=CancellationToken seul ; 2=booléen global isCancelled remis à false au lancement de B ; 3=identifiant immuable par génération comparé à celui de la génération active avant chaque mise à jour UI. Réponds seulement en JSON : clé choice, valeur entière.'},
    {"id": "generation-guard", "kind": "code", "method": "ShouldApply",
     "prompt": 'Écris uniquement une méthode C# public static bool ShouldApply(Guid chunkGeneration, Guid activeGeneration, bool cancelled). Elle doit accepter un fragment si et seulement si les identifiants correspondent ET cancelled est false. Aucune autre méthode, classe, using ou explication. Le code sera inséré dans une classe existante. Le corps doit rester une fonction pure.'},
    {"id": "stream-cancellation", "kind": "code", "method": "Forward",
     "prompt": 'Écris uniquement une méthode C# public static async IAsyncEnumerable<string> Forward(IAsyncEnumerable<string> source, [EnumeratorCancellation] CancellationToken cancellationToken = default). Elle transmet les éléments de source en propageant le token à son énumération et vérifie le token avant chaque yield return. Ne masque aucune exception. Pas de Task.Run ni de blocage synchrone. Aucune autre méthode, classe, using ou explication. Les namespaces System, System.Collections.Generic, System.Runtime.CompilerServices, System.Threading et System.Threading.Tasks sont déjà importés.'},
]


def model_for(key):
    return MANIFEST["model"] if key == "8b-q4" else MANIFEST["candidates"][key]


def json_answer(text):
    text = text.strip()
    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?\s*|\s*```$", "", text)
    return json.loads(text)


def answer(port, task, plan=None):
    prompt = task["prompt"]
    if plan is not None:
        prompt = ("Résous la demande originale ci-dessous. Les notes d'un autre modèle sont des hypothèses "
                  "non vérifiées : vérifie-les et corrige-les si nécessaire. Respecte strictement le format "
                  "de sortie demandé, sans recopier les notes.\n\nDEMANDE ORIGINALE:\n" + prompt +
                  "\n\nNOTES À VÉRIFIER:\n" + plan)
    result = chat(port, prompt, limit=512 if task["kind"] == "code" else 256, sampling=SAMPLING)
    if task["kind"] == "json":
        try:
            result["parsed"] = json_answer(result["content"])
            expected = task["expected"]
            parsed = result["parsed"]
            types_ok = isinstance(parsed, dict) and all(
                name in parsed and isinstance(parsed[name], bool) == isinstance(value, bool)
                for name, value in expected.items())
            result["correct"] = parsed == expected and types_ok and not result["output_truncated"]
        except (ValueError, TypeError):
            result["correct"] = False
    else:
        result["needs_code_review"] = True
    return result


@contextmanager
def server(key, port, output, measurements, flash="on", context=8192, cpu_moe=0,
           gpu_layers=99, reasoning="off", reasoning_budget=256, reasoning_effort="default", moe_cache_mib=0,
           cpu_ffn=0, cache_type="f16"):
    model = model_for(key)
    path = LOCAL / "models" / model["filename"]
    if not path.is_file():
        raise RuntimeError(f"Missing model {key}; run setup.py --model {key} first")
    try:
        request(port, "GET", "/health")
    except OSError:
        pass
    else:
        raise RuntimeError("Port already serves HTTP")
    log_path = output.with_name(output.stem + f".{key}.server.log")
    measurements.update({"model": model, "gpu_before": gpu(), "server_log": str(log_path),
                         "settings": {"context":context,"cpu_moe":cpu_moe,"gpu_layers":gpu_layers,
                                      "reasoning":reasoning,"reasoning_budget":reasoning_budget,
                                      "reasoning_effort":reasoning_effort,"batch":512,"ubatch":128,
                                      "moe_cache_mib":moe_cache_mib,"cpu_ffn":cpu_ffn,"cache_type":cache_type}})
    process = None
    with log_path.open("w") as log:
        try:
            start = time.perf_counter()
            process = subprocess.Popen([sys.executable, str(Path(__file__).with_name("run.py")),
                                        "--model", str(path), "--context", str(context), "--flash", flash,
                                        "--cpu-moe", str(cpu_moe), "--gpu-layers", str(gpu_layers),
                                        "--moe-cache-mib", str(moe_cache_mib),
                                        "--cpu-ffn", str(cpu_ffn), "--cache-type", cache_type,
                                        "--reasoning", reasoning, "--reasoning-budget", str(reasoning_budget),
                                        "--reasoning-effort", reasoning_effort,
                                        "--port", str(port)], stdout=log, stderr=subprocess.STDOUT)
            while time.perf_counter() - start < 180:
                if process.poll() is not None:
                    raise RuntimeError(f"Server {key} exited {process.returncode}: {log_path}")
                try:
                    if request(port, "GET", "/health")[0] == 200:
                        break
                except OSError:
                    pass
                time.sleep(0.2)
            else:
                raise RuntimeError("Startup timeout")
            measurements["startup_to_ready_seconds"] = time.perf_counter() - start
            measurements["pid"] = process.pid
            log_text=log_path.read_text(errors="replace")
            if not re.search(r"offloaded (\d+)/\1 layers to GPU", log_text) and gpu_layers == 99:
                raise RuntimeError("Full GPU layer offload not confirmed")
            measurements["offload_log"] = [line for line in log_text.splitlines()
                if any(word in line for word in ("offloaded", "model buffer size", "KV buffer size"))]
            measurements["gpu_loaded"] = gpu()
            from resources import ResourceSamples
            with ResourceSamples(process.pid) as resources:
                measurements["resources"] = resources
                yield
            measurements["gpu_after_requests"] = gpu()
            measurements["host_memory_after_requests"] = host_memory(process.pid)
        finally:
            if process is not None:
                start = time.perf_counter()
                process.terminate()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
                    measurements["forced_shutdown"] = True
                measurements["shutdown_seconds"] = time.perf_counter() - start
                measurements["exit_code"] = process.returncode
                measurements["gpu_after_shutdown"] = gpu()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    choices = ["8b-q4", *MANIFEST["candidates"]]
    parser.add_argument("--models", nargs="+", choices=choices, default=["8b-q4", "coder7b-q6"])
    parser.add_argument("--planner", choices=choices, default="8b-q4")
    parser.add_argument("--coder", choices=choices, default="coder7b-q6")
    parser.add_argument("--port", type=int, default=18080)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/m1/candidate-evaluation.json")
    args = parser.parse_args()
    SAMPLING["seed"] = args.seed
    args.output.parent.mkdir(parents=True, exist_ok=True)
    order = list(dict.fromkeys([args.planner, *args.models, args.coder]))
    report = {"timestamp": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "backend": MANIFEST["backend"],
              "sampling": SAMPLING, "context": 8192, "tasks": TASKS,
              "planner": args.planner, "coder": args.coder, "runs": {}}
    plans = {}
    start = time.perf_counter()
    try:
        for key in order:
            run = report["runs"][key] = {"direct": {}, "plans": {}, "planned_answers": {}}
            with server(key, args.port, args.output, run):
                for task in TASKS:
                    print(f"{key}: direct {task['id']}", flush=True)
                    run["direct"][task["id"]] = answer(args.port, task)
                if key == args.planner:
                    for task in TASKS:
                        print(f"{key}: prepare {task['id']}", flush=True)
                        policy = ("Ton rôle est de préparer des notes de travail pour un autre modèle chargé de résoudre "
                                  "une demande C#. Identifie les règles du langage ou invariants à vérifier et les pièges. "
                                  "Maximum 120 mots. Ne donne ni code final ni réponse finale. N'invente pas d'API. "
                                  "Le format de sortie demandé dans la demande utilisateur concerne le modèle suivant ; "
                                  "tes propres notes sont du texte libre. Tes hypothèses seront vérifiées par ce modèle.")
                        plan = chat(args.port, task["prompt"], limit=256, sampling=SAMPLING, system=policy)
                        plans[task["id"]] = plan["content"]
                        run["plans"][task["id"]] = plan
                if key == args.coder:
                    for task in TASKS:
                        print(f"{key}: with plan {task['id']}", flush=True)
                        run["planned_answers"][task["id"]] = answer(args.port, task, plans[task["id"]])
            args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        report["batch_wall_seconds"] = time.perf_counter() - start
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
        print(f"Report: {args.output}", flush=True)
    for key, run in report["runs"].items():
        for mode in ("direct", "planned_answers"):
            scored = [result for result in run[mode].values() if "correct" in result]
            if scored:
                print(key, mode, sum(result["correct"] for result in scored), "/", len(scored))
        if run.get("forced_shutdown") or run["exit_code"] != 0:
            raise RuntimeError(f"Unclean server shutdown for {key}")


if __name__ == "__main__":
    main()
