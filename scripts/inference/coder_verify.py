"""Independent compiler/tests and closed architecture constraints for the solo bench."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

from coder_cases import PRELUDE
from evaluate import json_answer

DOTNET = Path(shutil.which("dotnet")).resolve()
DOTNET_ROOT = DOTNET.parent
HARNESS = r"""
public static class Program {
 static readonly List<string> failures=new(); static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)failures.Add(name);}
 static void Near(double actual,double expected,string name)=>Check(double.IsFinite(actual)&&Math.Abs(actual-expected)<=1e-12*Math.Max(1,Math.Abs(expected)),$"{name}: expected {expected:R}, got {actual:R}");
 static void Throws<T>(Action action,string name) where T:Exception {try{action();Check(false,name+": no exception");}catch(T){Check(true,name);}catch(Exception e){Check(false,name+": wrong "+e.GetType().Name);}}
 static IEnumerable<string[]> Permutations(string[] a){if(a.Length==0){yield return Array.Empty<string>();yield break;}for(int i=0;i<a.Length;i++)foreach(var p in Permutations(a.Where((v,j)=>j!=i).ToArray()))yield return new[]{a[i]}.Concat(p).ToArray();}
 public static async Task Main(){bool completed=false;try{
BODY
 completed=true;}catch(Exception e){failures.Add("Unexpected "+e.GetType().Name+": "+e.Message);}
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=completed&&failures.Count==0,completed,checks,failures}));}
}
public sealed class Probe:IAsyncEnumerable<string>{
 public bool ThrowAfterFirst;public int Enumerations,Disposals;public CancellationToken Token;
 public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token=default){Enumerations++;Token=token;return new E(this);}
 sealed class E:IAsyncEnumerator<string>{readonly Probe p;int position;public E(Probe p){this.p=p;}public string Current=>position==1?"first":"late";
 public ValueTask<bool> MoveNextAsync(){position++;if(p.ThrowAfterFirst&&position==2)throw new InvalidOperationException("source failure");return ValueTask.FromResult(position<=2);}
 public ValueTask DisposeAsync(){p.Disposals++;return ValueTask.CompletedTask;}}
}
"""


def source(text):
    text = text.strip()
    if text.startswith("```"):
        lines = text.splitlines()
        if lines[-1].strip() == "```":
            return "\n".join(lines[1:-1])
    return text


def verify_code(case, code, output):
    start = time.perf_counter()
    output.mkdir(parents=True, exist_ok=True)
    (output / "method.cs.txt").write_text(code)
    # The model supplies only source text, never a project, dependency or command.
    with tempfile.TemporaryDirectory(prefix="oryn-coder-solo-") as directory:
        folder = Path(directory)
        (folder / "Probe.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<OutputType>Exe</OutputType><Nullable>enable</Nullable><ImplicitUsings>disable</ImplicitUsings>'
            '<EnableNETAnalyzers>false</EnableNETAnalyzers><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
            '</PropertyGroup><ItemGroup><Compile Include="Generated.cs"/><Compile Include="Harness.cs"/></ItemGroup></Project>')
        (folder / "Generated.cs").write_text(PRELUDE + "public static class Solution {\n" + source(code) + "\n}\n")
        (folder / "Harness.cs").write_text(PRELUDE + HARNESS.replace("BODY", case["tests"]))
        build = subprocess.run([str(DOTNET), "build", str(folder / "Probe.csproj"), "--nologo",
                                "--disable-build-servers"], capture_output=True, text=True, timeout=60)
        compiler = build.stdout + build.stderr
        (output / "compiler.log").write_text(compiler)
        result = {"compiled": build.returncode == 0, "compiler_output": compiler[-10000:], "passed": False}
        if build.returncode == 0:
            binary = folder / "bin/Debug/net10.0"
            # CoreCLR uses sizable memory-backed files while loading assemblies;
            # a 1 MiB RLIMIT_FSIZE prevents even System.Private.CoreLib loading.
            command = ["prlimit", "--cpu=5", "--fsize=134217728", "bwrap", "--unshare-all",
                       "--die-with-parent", "--new-session", "--cap-drop", "ALL",
                       "--ro-bind", "/usr", "/usr", "--symlink", "usr/bin", "/bin",
                       "--symlink", "usr/lib", "/lib", "--symlink", "usr/lib64", "/lib64",
                       "--ro-bind", str(DOTNET_ROOT), "/dotnet", "--ro-bind", str(binary), "/work",
                       "--dir", "/etc", "--ro-bind", "/etc/ld.so.cache", "/etc/ld.so.cache",
                       "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp", "--clearenv",
                       "--setenv", "PATH", "/dotnet:/usr/bin", "--setenv", "DOTNET_EnableDiagnostics", "0",
                       "--setenv", "DOTNET_GCHeapHardLimit", "0x10000000", "--chdir", "/work",
                       "/dotnet/dotnet", "/work/Probe.dll"]
            with (output / "runtime.log").open("w+") as log:
                try:
                    run = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=10)
                    result["runtime_exit_code"] = run.returncode
                except subprocess.TimeoutExpired:
                    result["timeout"] = True
                log.seek(0)
                runtime = log.read(1048576)
            result["runtime_output"] = runtime[-10000:]
            try:
                data = json.loads(runtime.strip().splitlines()[-1])
                result["tests"] = data
                result["passed"] = result.get("runtime_exit_code") == 0 and data["passed"] and data["checks"] > 0
            except (ValueError, IndexError, KeyError, TypeError):
                result["infrastructure_or_runtime_failure"] = True
        result["verification_seconds"] = time.perf_counter() - start
    (output / "verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    return result


def verify_answer(case, text):
    try:
        parsed = json_answer(text)
    except (ValueError, TypeError):
        return {"passed": False, "failures": ["Invalid JSON answer"]}
    if case["category"] == "diagnosis":
        expected = case["expected"]
        ok = parsed == expected and isinstance(parsed, dict) and all(
            type(parsed.get(k)) is type(v) for k, v in expected.items())
        return {"passed": ok, "parsed": parsed, "expected": expected}
    failures = []
    if not isinstance(parsed, dict):
        return {"passed": False, "failures": ["Expected JSON object"]}
    if case["id"] == "bounded-ui-pipeline":
        capacity = parsed.get("capacity")
        if type(capacity) is not int or not 1 <= capacity <= 3:
            failures.append("capacity must be positive and 2*(capacity+1) <= 8 MiB")
        expected = {"overflow":"wait","producer":"background","state_writer":"ui","stale_guard":"generation_id"}
    elif case["id"] == "durable-chat-order":
        order = parsed.get("order", [])
        actions = {"begin","save_user","save_pending","commit","infer"}
        if not isinstance(order, list) or len(order) != 5 or any(not isinstance(a,str) for a in order) or set(order) != actions:
            failures.append("order must contain exactly the five actions")
        elif not (order.index("begin") < order.index("save_user") < order.index("commit") < order.index("infer")
                  and order.index("begin") < order.index("save_pending") < order.index("commit")):
            failures.append("transactional partial order violated")
        expected = {"storage_failure":"keep_draft_no_infer","restart_status":"interrupted","keep_partial":True,"auto_resend":False}
    else:
        expected = {"old_identity":"preserve_unresolved","chat":"keep_on_U","human_status":"preserve","mark_implemented":False}
        # The supplied prompt asks for this field but does not require enabling
        # manual linking. Do not silently grade an unstated product requirement.
        if type(parsed.get("manual_link")) is not bool:
            failures.append("manual_link must be a boolean")
    for name,value in expected.items():
        if type(parsed.get(name)) is not type(value) or parsed.get(name) != value:
            failures.append("constraint violated: " + name)
    return {"passed": not failures, "parsed": parsed, "failures": failures}
