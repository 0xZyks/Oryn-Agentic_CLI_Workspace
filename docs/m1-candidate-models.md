# M1: Coder candidate and a sequential two-model experiment

Date: 2026-10-15. This is an offline/local PoC, not Oryn's agent runtime.
The graphical chat still uses the M0 demo. GPT-OSS was deferred during the
experiments below; the user subsequently reopened it. See the later
[GPT-OSS evaluation](m1-gptoss-evaluation.md) and
[Qwen3-Coder-30B experiment](m1-large-coder-evaluation.md).
**Gemma has not been downloaded or tested: official access is blocked.**

The subsequent [individual Coder evaluation](m1-coder-solo-evaluation.md) uses
executable problems and stricter independent verification. Its findings supersede
the encouraging interpretation of the small toy set below; keep those earlier
scores as historical observations, not an acceptance result.

## Artifacts and access

`manifest.json` now pins these additional candidates:

| Candidate | Official revision | File | License/access |
|---|---|---:|---|
| Qwen2.5-Coder-7B Instruct Q6_K | `13fb94bfda8c8cf22497dc57b78f391a9acb426a` | 6,254,198,784 bytes | Apache-2.0; downloaded and SHA-256 verified |
| Gemma 3 12B IT QAT Q4_0 | `7929e34ac7c459bbfd1f38d7358e6839f204578b` | 8,074,473,920 bytes | Gemma terms; official download returned HTTP 401 |

Gemma requires the user's accepted terms and an authorized Hugging Face account.
No HF token was present in the environment or standard local token file when
checked. Access remains pending user action; no terms were accepted or account
created by the agent, and no alternative mirror was used to bypass the gate.
[Official Gemma repository and access conditions](https://huggingface.co/google/gemma-3-12b-it-qat-q4_0-gguf).

After the user has configured access, `setup.py` can use `HF_TOKEN`,
`HUGGING_FACE_HUB_TOKEN`, `HF_TOKEN_PATH`, or the standard `HF_HOME/token` file
(default `~/.cache/huggingface/token`). It passes the header to curl on stdin,
not in argv or logs. This authenticated download path is prepared but has not
been verified with a real account. Do not put credentials in the repository or
conversation. Downloads communicate only with the model host; prompts stay local.

```sh
python3 scripts/inference/setup.py --model coder7b-q6
python3 scripts/inference/setup.py --model coder7b-q6 --verify-only
# Requires Gemma access first:
python3 scripts/inference/setup.py --model gemma12b-qat-q4
```

All servers must be stopped before setup/verification; the installation lock
prevents re-extraction of live libraries. Model files stay under ignored `.local`.
Q6_K was selected for the Coder to reduce quantization loss while preserving
memory headroom. It is not the same quantization as the Qwen3 Q4 baselines, so
differences cannot be attributed solely to model architecture or specialization.

## Coder transport, memory and long input

Tested on the same Debian 13 / Ryzen 5 5500 / RTX 3060 12 GiB / driver 550.163.01
machine, backend b11658 CUDA 12.8. Flash Attention on, F16 KV, 8192 context, one
slot, host prompt snapshot cache disabled. Coder sampling: temperature 0.7,
top-p 0.8, top-k 20, min-p 0, presence penalty 0. Separate reasoning is not
declared/simulated for Coder or Gemma. Their arithmetic probe uses ordinary text.
These profiles are explicit experimental settings, not a claim of optimality.

```sh
python3 scripts/inference/smoke.py --model .local/inference/models/qwen2.5-coder-7b-instruct-q6_k.gguf --flash on --context 8192 --extended --output artifacts/m1/coder7b-q6-extended-8192.json
```

Observed in one run:

- Approximately **44.1 tokens/s**, first content **0.060 / 0.046 s** on the two
  short prompts; all 29/29 layers offloaded to GPU.
- Sampled total GPU memory after requests: **6913 MiB (6.75 GiB)**, versus
  approximately 10.15 GiB for the historical 14B Q4 run. These are samples, not
  peak GPU measurements. Model buffer 5532 MiB, KV buffer 448 MiB.
- Process RSS after requests: 905196 KiB (0.86 GiB); process high-water RSS
  6429856 KiB (6.13 GiB), including loading/file-backed mmap pages.
- A synthetic 5597-token input with zero cached tokens: correct key retrieval,
  first content **3.00 s**, total **3.38 s**, 17 output tokens. This is a
  tokenizer-sized workload, not byte-identical input to the earlier 5580-token
  Qwen3 workloads.
- Arithmetic result 34 correct, 161 ordinary output tokens, **3.68 s** total;
  no separately streamed reasoning. The initial report's nominal budget field
  reflects the server configuration, not an enabled reasoning capability.
- Streaming, finish/DONE, disconnect followed by idle/stable counter, next
  request, malformed request HTTP 400 and graceful shutdown all pass. GPU returns
  to about 646 MiB. No server is left running.

The earlier short C# probe still produced an incorrect default-interface answer
and a weak free-form cancellation design. Different wording/sampling in the
controlled evaluation below produces different results; neither is a general
accuracy estimate.

## Direct versus prepared answers

The user suggested combining a model that prepares a request with a specialized
Coder. `evaluate.py` tests a **sequential Qwen3-8B Q4 → Coder-7B Q6** pair. The
8B is fully unloaded before Coder starts; no simultaneous residency is assumed.
The Coder is told that preparatory notes are fallible and must be verified.
This creates no production scheduler, tools or multi-agent proposal pipeline.

Eight fixed synthetic requests: six C# questions with known answers, and two
small method-generation requests (generation-identity guard and cancellable
async stream forwarding). The six answers cover default interface methods,
loop-variable capture, deferred LINQ, integer division, exception filters and
late-generation fragments. Ground truth for the four executable language snippets
was run with .NET 10 in a temporary project: outputs `333`, `6`, `2`, `1`.
The default-interface answer was compiler-verified in the preceding experiment.

All direct and solver requests use temperature 0.2, top-p 0.95, top-k 40, min-p 0,
presence penalty 0, thinking off. Each six-question score is **semantic answer
accuracy**: one enclosing Markdown JSON fence is accepted/removed; this is not
strict raw-JSON conformance or schema-constrained generation. The two generated
methods are assessed separately, not folded into the six-question score.

The first preliminary experiment put preparer instructions and the original
request in one user message; scores were 8B 2/6, Coder 5/6, pair 4/6. The follow-up
uses a separate system policy for the preparer and repeats with three seeds:

| Six identical questions, repeated | Seed 42 | Seed 7 | Seed 123 |
|---|---:|---:|---:|
| Qwen3-8B Q4 direct | 2/6 | 2/6 | 2/6 |
| Coder-7B Q6 direct | 5/6 | 4/6 | 4/6 |
| 8B preparation → Coder | 3/6 | 3/6 | 3/6 |

These are six repeated questions, not 18 independent questions. The original
requests' formatting instructions still sometimes distract the 8B preparer, and
its notes contain factual mistakes. On the loop-capture problem, the Coder's
correct direct answer becomes incorrect after receiving the notes. This is
evidence against **this pairing/prompt configuration**, not against multi-model
workflows in general. No Gemma preparer or 14B preparer has been measured here.

For the whole eight-request set, Coder-only generation calls total about
**4.65–4.67 s**; preparation plus Coder calls total **10.56–10.95 s**. Those sums
exclude startup/shutdown, networking outside localhost, and real user pacing.
They are not per-question end-to-end latency measurements. Model readiness in
these cached runs adds roughly 1.3–1.8 s per loaded model, with shutdown around
0.2 s; residency/switch cost can vary. The actual measured batch wall time is
stored in each report and includes direct baselines as well as chained calls.

### Generated-method verification

`check_code.py` compiles methods in fixed temporary .NET 10 projects, without
model-controlled packages, analyzers or build files. Unknown methods are only
compiled. Behavioral execution is limited to exact normalized variants manually
reviewed in the script (a pure GUID/bool guard and the two simple async iterator
forms); other generated code is never executed by this checker.

Across all three final seeds:

- All nine method pairs compile (three modes × three seeds).
- All generation guards pass the matching/cancelled truth table.
- Both direct models pass cancellation against a source that deliberately ignores
  the token, thanks to an explicit `ThrowIfCancellationRequested()` before yield.
- **All three chained answers omit that explicit check and fail this behavior
  test**, although they compile. This violates an explicit original requirement.

The generation prompts spell out their invariants. Passing them does not prove
the model can design the same behavior autonomously or handle arbitrary code.

## Reproduce and next step

```sh
python3 scripts/inference/evaluate.py --seed 42 --output artifacts/m1/candidate-evaluation-role-seed42.json
python3 scripts/inference/evaluate.py --seed 7 --output artifacts/m1/candidate-evaluation-role-seed7.json
python3 scripts/inference/evaluate.py --seed 123 --output artifacts/m1/candidate-evaluation-role-seed123.json
python3 scripts/inference/check_code.py artifacts/m1/candidate-evaluation-role-seed42.json artifacts/m1/candidate-evaluation-role-seed7.json artifacts/m1/candidate-evaluation-role-seed123.json
```

Reports and server logs are in ignored `artifacts/m1`; code-check results use the
suffix `.code-check.json`. The initial preliminary run remains
`candidate-evaluation.json`. Exact artifacts, questions, outputs, settings and
timings are preserved. Syntax checks, CLI help and `git diff --check` were also
run. No Oryn application code or solution build changed in this experiment.

Coder is a useful candidate for the coding role. This particular automatic
preparation chain is slower and less accurate on the small test set, so do not
enable it by default. Favor measured routing and deterministic compiler/tests;
evaluate a stronger/complementary preparer later. Gemma testing resumes once its
official access is configured. Do not substitute another Gemma version or claim
Gemma results from these Qwen measurements.
