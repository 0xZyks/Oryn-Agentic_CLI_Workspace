"""Compile methods; behavior-test only exact, previously reviewed safe variants."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile

GUARDS = [
    "public static bool ShouldApply(Guid chunkGeneration, Guid activeGeneration, bool cancelled) { return chunkGeneration == activeGeneration && !cancelled; }",
    "public static bool ShouldApply(Guid chunkGeneration, Guid activeGeneration, bool cancelled) => chunkGeneration == activeGeneration && !cancelled;",
]
FORWARD = """public static async IAsyncEnumerable<string> Forward(IAsyncEnumerable<string> source,
[EnumeratorCancellation] CancellationToken cancellationToken = default) {
await foreach (var item in source.WithCancellation(cancellationToken)) {
CHECK
yield return item;
} }"""
PRELUDE = ("using System;\nusing System.Collections.Generic;\nusing System.Runtime.CompilerServices;\n"
           "using System.Threading;\nusing System.Threading.Tasks;\n")
HARNESS = """
public static class Program {
 public static async Task Main() {
  var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
  var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
  bool guard = true;
  foreach (bool match in new[] {false,true}) foreach (bool cancelled in new[] {false,true})
   guard &= Generated.ShouldApply(a, match?a:b, cancelled) == (match && !cancelled);
  using var cts = new CancellationTokenSource();
  await using var e = Generated.Forward(IgnoresCancellation(), cts.Token).GetAsyncEnumerator();
  bool first = await e.MoveNextAsync();
  cts.Cancel();
  bool stopped = false;
  try { await e.MoveNextAsync(); } catch (OperationCanceledException) { stopped = true; }
  Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
    guard_truth_table_correct = guard, uncooperative_source_cancelled = first && stopped,
    behavior_passed = guard && first && stopped }));
 }
 private static async IAsyncEnumerable<string> IgnoresCancellation() {
  await Task.CompletedTask; yield return "first"; yield return "late";
 }
}
"""


def normalize(text):
    return re.sub(r"\s+", "", text)


def unfence(text):
    text = text.strip()
    return re.sub(r"^```(?:csharp|cs)?\s*|\s*```$", "", text) if text.startswith("```") else text


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reports", nargs="+", type=Path)
    args = parser.parse_args()
    for path in args.reports:
        report = json.loads(path.read_text())
        code_tasks = [task["id"] for task in report["tasks"] if task["kind"] == "code"]
        results = {}
        for model, run in report["runs"].items():
            for mode in ("direct", "planned_answers"):
                if not run[mode]:
                    continue
                methods = [unfence(run[mode][task]["content"]) for task in code_tasks]
                reviewed = (len(methods) == 2 and normalize(methods[0]) in {normalize(code) for code in GUARDS}
                            and normalize(methods[1]) in {normalize(FORWARD.replace("CHECK", check))
                                for check in ("", "cancellationToken.ThrowIfCancellationRequested();")})
                # Compile only against a fixed project with no model-controlled
                # build files, packages or analyzers. Execution is restricted to
                # the exact pure/streaming method variants reviewed above.
                with tempfile.TemporaryDirectory(prefix="oryn-m1-code-") as directory:
                    folder = Path(directory)
                    (folder / "Probe.csproj").write_text(
                        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                        f'<OutputType>{"Exe" if reviewed else "Library"}</OutputType><Nullable>enable</Nullable>'
                        '<EnableNETAnalyzers>false</EnableNETAnalyzers>'
                        '</PropertyGroup></Project>')
                    (folder / "Generated.cs").write_text(
                        PRELUDE + "public static class Generated {\n" +
                        "\n\n".join(methods) + "\n}\n" + (HARNESS if reviewed else ""))
                    build = subprocess.run(["dotnet", "build", str(folder / "Probe.csproj"), "--nologo",
                                            "--disable-build-servers"], capture_output=True, text=True, timeout=60)
                    result = results[model + "/" + mode] = {"compiled": build.returncode == 0,
                                                            "compiler_output": build.stdout + build.stderr,
                                                            "reviewed_safe_variant": reviewed, "executed": False}
                    if reviewed and build.returncode == 0:
                        executed = subprocess.run(["dotnet", str(folder / "bin/Debug/net10.0/Probe.dll")],
                                                  capture_output=True, text=True, timeout=10)
                        result["executed"] = True
                        result["runtime_exit_code"] = executed.returncode
                        result["runtime_output"] = executed.stdout + executed.stderr
                        if executed.returncode == 0:
                            result.update(json.loads(executed.stdout))
                    print(path.name, model, mode, "COMPILED" if build.returncode == 0 else "FAILED",
                          "behavior:", result.get("behavior_passed", "not executed"), flush=True)
        output = path.with_name(path.stem + ".code-check.json")
        output.write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n")


if __name__ == "__main__":
    main()
