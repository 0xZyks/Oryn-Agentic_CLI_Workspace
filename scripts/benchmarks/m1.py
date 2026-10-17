"""Prepare isolated Oryn prompts and grade captured JSON answers; no inference."""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import statistics
import sys

ROOT = Path(__file__).resolve().parents[2]
SUITE = ROOT / "benchmarks/m1/cases.json"
METRICS = (
    "first_visible_seconds", "end_to_end_seconds", "load_seconds", "input_tokens",
    "output_tokens", "reasoning_tokens", "sampled_gpu_used_mib", "sampled_rss_mib",
    "cancel_seconds",
)
FIELDS = ("case_id", "model_id", "condition", *METRICS, "notes")


def reject_constant(value):
    raise ValueError(f"Non-finite JSON constant: {value}")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def parse_json(text):
    return json.loads(text, object_pairs_hook=unique_object, parse_constant=reject_constant)


def read_text(path, limit=2 * 1024 * 1024):
    if path.stat().st_size > limit:
        raise ValueError(f"File exceeds {limit} bytes: {path}")
    return path.read_text(encoding="utf-8-sig")


def dump(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def validate_suite(suite):
    seen = set()
    for case in suite["cases"]:
        identifier = case["id"]
        if not re.fullmatch(r"[A-Z][0-9]{2}", identifier) or identifier in seen:
            raise ValueError("Invalid/duplicate case ID in suite")
        seen.add(identifier)
        if not isinstance(case["expected"], dict) or not case["prompt"]:
            raise ValueError(f"Invalid case: {identifier}")
    if not seen:
        raise ValueError("Empty suite")


def same(actual, expected):
    """Strict JSON types: True is not the integer 1; arrays are ordered."""
    if type(actual) is not type(expected):
        return False
    if isinstance(expected, dict):
        return actual.keys() == expected.keys() and all(same(actual[k], v) for k, v in expected.items())
    if isinstance(expected, list):
        return len(actual) == len(expected) and all(same(a, b) for a, b in zip(actual, expected))
    return actual == expected


def prepare(args):
    suite_bytes = SUITE.read_bytes()
    suite = parse_json(suite_bytes.decode("utf-8"))
    validate_suite(suite)
    config_path = args.config.resolve()
    settings = parse_json(read_text(config_path))
    if not isinstance(settings, dict):
        raise ValueError("Configuration must be an object")
    # WorkspaceSettings accepts case-insensitive properties. Reject ambiguous spelling.
    keys = {}
    for key in settings:
        if key.lower() in keys:
            raise ValueError(f"Case-insensitive duplicate setting: {key}")
        keys[key.lower()] = key
    project_root = (args.project_root or config_path.parent).resolve()
    for name in ("inferencemodelpath", "inferencerunnerpath", "llamaserverpath", "inferenceassetsroot"):
        key = keys.get(name)
        if key and settings[key]:
            path = Path(settings[key])
            settings[key] = str((project_root / path).resolve() if not path.is_absolute() else path.resolve())
    profiles = settings.get(keys.get("modelprofiles", "ModelProfiles"), {})
    if not isinstance(profiles, dict):
        raise ValueError("ModelProfiles must be an object")
    selected = settings.get(keys.get("selectedmodelprofile", "SelectedModelProfile"), "core")
    profile = profiles.get(selected, {})
    for item in profiles.values():
        if not isinstance(item, dict):
            raise ValueError("Invalid model profile")
        for key in list(item):
            if key.lower() == "modelpath" and item[key]:
                item[key] = str((project_root / Path(item[key])).resolve())
    properties = {key.lower(): value for key, value in profile.items()}
    model = properties.get("modelpath") or settings.get(keys.get("inferencemodelpath", "InferenceModelPath"))
    if properties.get("catalogid") and not properties.get("modelpath"):
        manifest = parse_json(read_text(ROOT / "scripts/inference/manifest.json"))
        catalog = {"8b-q4": manifest["model"], **manifest["candidates"]}
        candidate = catalog.get(properties["catalogid"])
        if candidate is None:
            raise ValueError("Unknown CatalogId")
        assets = args.assets_root or settings.get(keys.get("inferenceassetsroot", "InferenceAssetsRoot"))
        if not assets and (ROOT / ".local/inference").is_dir():
            assets = ROOT / ".local/inference"
        if not assets:
            raise ValueError("Supply --assets-root for a CatalogId stored outside this checkout")
        assets = (project_root / Path(assets)).resolve()
        settings[keys.get("inferenceassetsroot", "InferenceAssetsRoot")] = str(assets)
        model = str(assets / "models" / candidate["filename"])
    if not model or not Path(model).is_file():
        raise ValueError("Select an installed model before preparing this run")
    for name in ("inferencerunnerpath", "llamaserverpath"):
        key = keys.get(name)
        if key and settings[key] and not Path(settings[key]).is_file():
            raise ValueError(f"Configured file not found: {settings[key]}")
    # Scratch projects get their own history, never the source project's explicit data directory.
    settings[keys.get("chatdataroot", "ChatDataRoot")] = ".local/oryn/projects"
    output = args.output.resolve()
    if output.exists():
        raise ValueError("Output already exists; choose a fresh run directory to preserve captures")
    output.mkdir(parents=True)
    for folder in ("prompts", "answers", "workspaces"):
        (output / folder).mkdir()
    (output / "suite.json").write_bytes(suite_bytes)
    label = args.label or Path(model).stem
    dump(output / "run.json", {
        "schema_version": 1, "suite_version": suite["version"],
        "suite_sha256": hashlib.sha256(suite_bytes).hexdigest(),
        "prepared_at": datetime.now(timezone.utc).isoformat(),
        "source_project_root": str(project_root), "source_config": str(config_path),
        "model_label": label, "condition": args.condition,
        "model_hash": None, "oryn_revision": None,
        "measurement_method": "user-captured; no automatic instrumentation",
    })
    with (output / "measurements.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=FIELDS)
        writer.writeheader()
        for case in suite["cases"]:
            identifier = case["id"]
            (output / "prompts" / f"{identifier}.txt").write_text(case["prompt"] + "\n", encoding="utf-8")
            (output / "answers" / f"{identifier}.txt").write_text("", encoding="utf-8")
            workspace = output / "workspaces" / identifier
            workspace.mkdir()
            dump(workspace / "oryn.settings.json", settings)
            writer.writerow({"case_id": identifier, "model_id": label, "condition": args.condition})
    (output / "INSTRUCTIONS.txt").write_text(
        "Open each workspaces/<ID> folder in the current Oryn build. Paste prompts/<ID>.txt.\n"
        "Copy only the final answer to answers/<ID>.txt. Record actual measurements in measurements.csv.\n"
        "Each workspace starts without chat history; use a new prepared run for repetitions.\n"
        "Unknown measurements stay blank. Model/condition labels are user declarations.\n"
        "See docs/m1-completion-and-validation.md in the Oryn repository, then run:\n"
        f'python3 scripts/benchmarks/m1.py grade --run "{output}"\n', encoding="utf-8")
    print(f"Prepared {len(suite['cases'])} isolated cases in {output}")
    print("No model, test or benchmark was run. Capture answers in Oryn, then use grade.")
    return 0


def answer_json(text):
    text = text.strip()
    if text.startswith("```"):
        match = re.fullmatch(r"```(?:json)?\s*\n(.*?)\n```", text, re.DOTALL | re.IGNORECASE)
        if not match:
            raise ValueError("Expected a single JSON object, optionally in one JSON fence")
        text = match.group(1)
    return parse_json(text)


def measurements(path, outcomes, method="user-captured; unknown fields excluded, not replaced with zero"):
    groups, issues, seen = {}, [], set()
    with path.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        if tuple(reader.fieldnames or ()) != FIELDS:
            raise ValueError("Unexpected measurements.csv header")
        for row in reader:
            identifier = row["case_id"]
            if identifier not in outcomes or identifier in seen:
                issues.append(f"Unknown/duplicate measurement ID: {identifier}")
                continue
            seen.add(identifier)
            group_key = json.dumps([row["model_id"], row["condition"]], ensure_ascii=False)
            group = groups.setdefault(group_key, {"model_id": row["model_id"], "condition": row["condition"],
                                                  "all_observations": {}, "passed_observations": {}})
            for name in METRICS:
                if not row[name] or not row[name].strip():
                    continue
                try:
                    value = float(row[name])
                    if not math.isfinite(value) or value < 0:
                        raise ValueError("Expected a finite nonnegative observation")
                except ValueError:
                    issues.append(f"Invalid measurement {identifier}/{name}")
                    continue
                group["all_observations"].setdefault(name, []).append(value)
                if outcomes[identifier] == "passed":
                    group["passed_observations"].setdefault(name, []).append(value)
    for identifier in outcomes.keys() - seen:
        issues.append(f"Missing measurement row: {identifier}")
    for group in groups.values():
        for kind in ("all_observations", "passed_observations"):
            group[kind] = {name: {"n": len(values), "median": statistics.median(values),
                                  "min": min(values), "max": max(values)}
                           for name, values in group[kind].items()}
    return {"method": method,
            "groups": list(groups.values()), "issues": issues}


def grade(args):
    run = args.run.resolve()
    metadata = parse_json(read_text(run / "run.json"))
    suite_bytes = (run / "suite.json").read_bytes()
    if hashlib.sha256(suite_bytes).hexdigest() != metadata["suite_sha256"]:
        raise ValueError("Suite snapshot changed since preparation")
    suite = parse_json(suite_bytes.decode("utf-8"))
    validate_suite(suite)
    cases, outcomes, domains = [], {}, {}
    for case in suite["cases"]:
        identifier = case["id"]
        path = run / "answers" / f"{identifier}.txt"
        result = {"id": identifier, "domain": case["domain"], "expected": case["expected"],
                  "rationale": case["rationale"], "answer_file": str(path)}
        try:
            text = read_text(path) if path.exists() else ""
            if not text.strip():
                result["status"] = "missing"
            else:
                actual = answer_json(text)
                result["actual"] = actual
                result["status"] = "passed" if same(actual, case["expected"]) else "wrong_answer_or_contract"
        except (ValueError, OSError) as error:
            result.update(status="invalid_answer", error=str(error))
        cases.append(result)
        outcomes[identifier] = result["status"]
        domain = domains.setdefault(case["domain"], {"passed": 0, "total": 0})
        domain["total"] += 1
        domain["passed"] += result["status"] == "passed"
    passed = sum(status == "passed" for status in outcomes.values())
    report = {"suite_version": suite["version"], "run_metadata": metadata,
              "scored_at": datetime.now(timezone.utc).isoformat(),
              "passed": passed, "total": len(cases), "missing": sum(s == "missing" for s in outcomes.values()),
              "domains": domains, "cases": cases, "measurements": measurements(run / "measurements.csv", outcomes,
                  metadata.get("measurement_method", "user-captured; unknown fields excluded, not replaced with zero")),
              "scope": "Strict public JSON checks only; no generated code executed and no inference performed."}
    dump(run / "report.json", report)
    print(f"{passed}/{len(cases)} passed; {report['missing']} missing. Report: {run / 'report.json'}")
    return 0 if passed == len(cases) and not report["measurements"]["issues"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    setup = commands.add_parser("prepare", help="Export prompts, capture files and scratch project settings")
    setup.add_argument("--config", type=Path, required=True)
    setup.add_argument("--project-root", type=Path)
    setup.add_argument("--assets-root", type=Path, help="Catalog model/runtime storage for an installed app")
    setup.add_argument("--output", type=Path, required=True)
    setup.add_argument("--label")
    setup.add_argument("--condition", choices=("cold", "warm", "unspecified"), default="unspecified")
    setup.set_defaults(action=prepare)
    check = commands.add_parser("grade", help="Grade answers manually captured from Oryn; no model calls")
    check.add_argument("--run", type=Path, required=True)
    check.set_defaults(action=grade)
    args = parser.parse_args()
    try:
        return args.action(args)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"m1: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
