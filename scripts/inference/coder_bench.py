"""Solo Coder evaluation with frozen oracles, real tests and one bounded repair."""
import argparse
import hashlib
import json
from pathlib import Path
import time

from coder_cases import VERSION, CASES, DIAGNOSTICS, ARCHITECTURE
from coder_verify import verify_code, verify_answer
from evaluate import server
from setup import ROOT, MANIFEST
from smoke import chat

SAMPLING = {"temperature":0.2,"top_p":0.95,"top_k":40,"min_p":0.0,"presence_penalty":0.0}
POLICY = ("Tu es un assistant d'ingénierie C#/.NET. Résous seul la demande fournie. "
          "Pour le code : renvoie uniquement la ou les méthodes demandées, sans classe ni using ; "
          "elles seront insérées dans une classe Solution. Namespaces disponibles : System, "
          "System.Collections.Generic, System.Linq, System.Text, System.Threading, "
          "System.Threading.Tasks, System.Runtime.CompilerServices. "
          "Pour une demande JSON : renvoie uniquement l'objet JSON. Respecte toutes les contraintes.")


def oracle_hash():
    digest = hashlib.sha256()
    for name in ("coder_cases.py","coder_verify.py"):
        digest.update(Path(__file__).with_name(name).read_bytes())
    return digest.hexdigest()


def diagnostic_proof():
    return {
        "id":"diagnostic-proof", "reference":r"""
public sealed class Key { public int Id;public override bool Equals(object? o)=>o is Key k&&k.Id==Id;public override int GetHashCode()=>Id; }
public static async Task<Dictionary<string,string>> Diagnose(){
var result=new Dictionary<string,string>();var k=new Key{Id=1};var d=new Dictionary<Key,string>();d.Add(k,"x");k.Id=2;result["key"]=d.ContainsKey(k).ToString();
int F(){int x=1;try{return x;}finally{x=2;}}result["finally"]=F().ToString();
var a=new TaskCompletionSource<int>();var b=new TaskCompletionSource<int>();var both=Task.WhenAll(a.Task,b.Task);a.SetException(new InvalidOperationException("x"));result["whenall"]=both.IsCompleted.ToString();b.SetResult(7);try{await both;}catch(Exception e){result["whenall_error"]=e.GetType().Name;}
var sem=new SemaphoreSlim(1,1);using var cts=new CancellationTokenSource();cts.Cancel();try{try{await sem.WaitAsync(cts.Token);}finally{sem.Release();}}catch(Exception e){result["sem_error"]=e.GetType().Name;}result["sem_count"]=sem.CurrentCount.ToString();return result;
} """, "tests":r"""
var v=await Solution.Diagnose();Check(v["key"]=="False","mutable key");Check(v["finally"]=="1","finally return");Check(v["whenall"]=="False","when-all waits all tasks");Check(v["whenall_error"]=="InvalidOperationException","when-all exception");Check(v["sem_error"]=="SemaphoreFullException","release masks cancellation");Check(v["sem_count"]=="1","semaphore count");
"""}


def self_test(output):
    output.mkdir(parents=True, exist_ok=True)
    report = {"version":VERSION,"oracle_sha256":oracle_hash(),"cases":{},"passed":False}
    for case in CASES:
        print("Oracle",case["id"],flush=True)
        good = verify_code(case,case["reference"],output / case["id"] / "reference")
        bad = verify_code(case,case["mutant"],output / case["id"] / "mutant")
        report["cases"][case["id"]] = {"reference":good,"mutant":bad}
    proof=diagnostic_proof()
    report["diagnostic_proof"] = verify_code(proof,proof["reference"],output / "diagnostic-proof")
    valid = [
        {"capacity":3,"overflow":"wait","producer":"background","state_writer":"ui","stale_guard":"generation_id"},
        {"order":["begin","save_pending","save_user","commit","infer"],"storage_failure":"keep_draft_no_infer","restart_status":"interrupted","keep_partial":True,"auto_resend":False},
        {"old_identity":"preserve_unresolved","chat":"keep_on_U","human_status":"preserve","mark_implemented":False,"manual_link":True},
    ]
    report["architecture_oracles"] = []
    for case,answer in zip(ARCHITECTURE,valid):
        good=verify_answer(case,json.dumps(answer))
        altered=dict(answer)
        if case["id"]=="bounded-ui-pipeline":altered["capacity"]=4
        elif case["id"]=="durable-chat-order":altered["order"]=["begin","save_user","infer","save_pending","commit"]
        else:altered["mark_implemented"]=True
        bad=verify_answer(case,json.dumps(altered))
        report["architecture_oracles"].append({"id":case["id"],"valid":good,"invalid":bad})
    report["passed"] = (all(v["reference"]["passed"] and v["mutant"]["compiled"] and not v["mutant"]["passed"] for v in report["cases"].values())
                         and report["diagnostic_proof"]["passed"]
                         and all(v["valid"]["passed"] and not v["invalid"]["passed"] for v in report["architecture_oracles"]))
    (output / "oracles.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
    if not report["passed"]:
        raise RuntimeError(f"Oracle self-test failed; inspect {output / 'oracles.json'} before any model evaluation")
    print("PASS: all reference solutions, negative controls and diagnostic proofs",flush=True)


def feedback(check):
    if not check.get("compiled",True):
        return "Le compilateur rejette le code :\n"+check["compiler_output"][-4500:]
    if "tests" in check:
        return "Tests indépendants échoués :\n"+json.dumps(check["tests"],ensure_ascii=False)[:4500]
    return "Vérification échouée :\n"+json.dumps(check,ensure_ascii=False)[:4500]


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test",action="store_true")
    parser.add_argument("--output",type=Path,default=ROOT / "artifacts/m1/coder-solo")
    parser.add_argument("--seeds",type=int,nargs="+",default=[42,7,123])
    parser.add_argument("--port",type=int,default=18080)
    parser.add_argument("--model",choices=("8b-q4", *MANIFEST["candidates"]),default="coder7b-q6")
    parser.add_argument("--context",type=int,default=8192)
    parser.add_argument("--cpu-moe",type=int,default=0)
    parser.add_argument("--moe-cache-mib",type=int,default=0)
    parser.add_argument("--gpu-layers",type=int,default=99)
    parser.add_argument("--cpu-ffn",type=int,default=0)
    parser.add_argument("--cache-type",choices=("f16","q8_0"),default="f16")
    parser.add_argument("--reasoning",choices=("on","off"),default="off")
    parser.add_argument("--reasoning-budget",type=int,default=-1)
    parser.add_argument("--reasoning-effort",choices=("default","low","medium","high"),default="default")
    parser.add_argument("--code-limit",type=int,default=1800)
    parser.add_argument("--json-limit",type=int,default=512)
    parser.add_argument("--model-sampling",action="store_true",help="Use candidate profile instead of legacy Coder sampling")
    parser.add_argument("--language",choices=("fr","en"),default="fr")
    parser.add_argument("--flash",choices=("on","off"),default="on")
    parser.add_argument("--oracles",type=Path,help="Existing successful self-test report for the frozen cases")
    parser.add_argument("--categories",nargs="+",choices=("implementation","diagnosis","architecture"))
    args=parser.parse_args()
    sampling_profile=SAMPLING
    if args.model_sampling:
        model=MANIFEST["model"] if args.model=="8b-q4" else MANIFEST["candidates"][args.model]
        sampling_profile={**model["sampling"],"min_p":0.0}
    args.output.mkdir(parents=True,exist_ok=True)
    if args.self_test:
        self_test(args.output / "self-test")
        return
    proof=json.loads((args.oracles or args.output / "self-test/oracles.json").read_text())
    if not proof["passed"] or proof["oracle_sha256"]!=oracle_hash():
        parser.error("Run --self-test for the current frozen oracle version first")
    tasks=[dict(case)for case in CASES+DIAGNOSTICS+ARCHITECTURE]
    policy=POLICY
    if args.language=="en":
        from coder_english import PROMPTS, POLICY as ENGLISH_POLICY
        tasks=[{**case,"prompt":PROMPTS[case["id"]]}for case in tasks]
        policy=ENGLISH_POLICY
    if args.categories:
        tasks=[case for case in tasks if case["category"] in args.categories]
    report={"version":VERSION,"oracle_sha256":oracle_hash(),"timestamp":time.strftime("%Y-%m-%dT%H:%M:%S%z"),
            "seeds":args.seeds,"sampling":sampling_profile,"system_policy":policy,"language":args.language,"flash":args.flash,
            "model":args.model,"code_limit":args.code_limit,"json_limit":args.json_limit,
            "tests_visible_to_model":False,"repair_sees_failure_messages":True,
            "repair_limit":1,"tasks":[{"id":c["id"],"category":c["category"],"prompt":c["prompt"]}for c in tasks],
            "runs":{},"server":{}}
    path=args.output / "results.json"
    start=time.perf_counter()
    try:
        with server(args.model,args.port,path,report["server"],flash=args.flash,context=args.context,
                    cpu_moe=args.cpu_moe,moe_cache_mib=args.moe_cache_mib,gpu_layers=args.gpu_layers,reasoning=args.reasoning,
                    cpu_ffn=args.cpu_ffn,cache_type=args.cache_type,
                    reasoning_budget=args.reasoning_budget,reasoning_effort=args.reasoning_effort):
            for seed in args.seeds:
                run=report["runs"][str(seed)]={}
                for case in tasks:
                    print(f"seed {seed}: {case['id']}",flush=True)
                    entry=run[case["id"]]={"category":case["category"]}
                    sampling={**sampling_profile,"seed":seed}
                    answer=chat(args.port,case["prompt"],limit=args.code_limit if case["category"]=="implementation" else args.json_limit,
                                thinking=args.reasoning=="on",sampling=sampling,system=policy,require_progressive=False)
                    entry["first"]=answer
                    directory=args.output / "checks" / str(seed) / case["id"]
                    check=(verify_code(case,answer["content"],directory / "first") if case["category"]=="implementation"
                           else verify_answer(case,answer["content"]))
                    if answer["output_truncated"]:check["passed"]=False;check["truncated"]=True
                    entry["first_check"]=check
                    if not check["passed"] and case["category"]=="implementation":
                        prompt=("Corrige ta proposition ci-dessous en respectant la demande originale. "
                                "Renvoie uniquement les méthodes corrigées.\nDEMANDE:\n"+case["prompt"]+
                                "\nPROPOSITION:\n"+answer["content"]+"\nRETOUR DÉTERMINISTE:\n"+feedback(check))
                        retry=chat(args.port,prompt,limit=args.code_limit,thinking=args.reasoning=="on",sampling=sampling,system=policy,require_progressive=False)
                        entry["repair"]=retry
                        repaired=verify_code(case,retry["content"],directory / "repair")
                        if retry["output_truncated"]:repaired["passed"]=False;repaired["truncated"]=True
                        entry["repair_check"]=repaired
                    path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
                    print("  first",check["passed"],"repair",entry.get("repair_check",{}).get("passed","n/a"),flush=True)
    except Exception as error:
        report["error"]=str(error)
        raise
    finally:
        report["batch_wall_seconds"]=time.perf_counter()-start
        path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
        print("Report:",path,flush=True)
    for seed,run in report["runs"].items():
        print("seed",seed,"first",sum(e["first_check"]["passed"]for e in run.values()),"/",len(run),
              "after repair",sum(e.get("repair_check",e["first_check"])["passed"]for e in run.values()))
    if report["server"].get("forced_shutdown") or report["server"].get("exit_code")!=0:
        raise RuntimeError("Server did not shut down cleanly")


if __name__=="__main__":
    main()
