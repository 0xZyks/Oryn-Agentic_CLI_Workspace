"""Regrade saved answers after an explicitly documented oracle correction."""
import argparse
import ast
import copy
import json
from pathlib import Path
import time

from coder_cases import VERSION, CASES, DIAGNOSTICS, ARCHITECTURE
from coder_bench import oracle_hash
from coder_verify import verify_code, verify_answer


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reports",nargs="+",type=Path)
    parser.add_argument("--snapshot",type=Path,required=True,help="Original case source retained before the correction")
    parser.add_argument("--oracles",type=Path,required=True)
    args=parser.parse_args()
    proof=json.loads(args.oracles.read_text())
    if not proof["passed"] or proof["oracle_sha256"]!=oracle_hash():
        parser.error("A successful self-test matching current oracles is required")
    module=ast.parse(args.snapshot.read_text())
    previous=next(ast.literal_eval(node.value)for node in module.body if isinstance(node,ast.Assign)
                  and any(isinstance(target,ast.Name)and target.id=="CASES"for target in node.targets))
    old={case["id"]:case for case in previous}
    cases={case["id"]:case for case in CASES+DIAGNOSTICS+ARCHITECTURE}
    for path in args.reports:
        start=time.perf_counter()
        report=copy.deepcopy(json.loads(path.read_text()))
        report["original_oracle_sha256"]=report["oracle_sha256"]
        report["oracle_sha256"]=oracle_hash()
        report["version"]=VERSION
        report["regraded_from"]=str(path)
        report["repair_feedback_protocol"]="v1, before the two unstated requirements were removed"
        report["regrade_notes"]="Same saved answers; unchanged executable tests reused; async cancellation re-executed; JSON constraints rechecked. No new model calls."
        for seed,run in report["runs"].items():
            for name,entry in run.items():
                case=cases[name]
                for attempt in ("first","repair"):
                    if attempt not in entry:continue
                    if case["category"]=="implementation":
                        if old[name]["tests"]==case["tests"]:
                            entry[attempt+"_check"]["reused_unchanged_tests"]=True
                            continue
                        check=verify_code(case,entry[attempt]["content"],path.parent / "regrade-v2" / seed / name / attempt)
                    else:
                        check=verify_answer(case,entry[attempt]["content"])
                    if entry[attempt]["output_truncated"]:check["passed"]=False;check["truncated"]=True
                    entry[attempt+"_check"]=check
        report["regrade_wall_seconds"]=time.perf_counter()-start
        output=path.with_name(path.stem+".regraded-v2.json")
        output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
        print(output,flush=True)
        for seed,run in report["runs"].items():
            print(seed,"first",sum(e["first_check"]["passed"]for e in run.values()),"/",len(run),
                  "after repair",sum(e.get("repair_check",e["first_check"])["passed"]for e in run.values()),flush=True)


if __name__=="__main__":
    main()
