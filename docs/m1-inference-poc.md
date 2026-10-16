# M1 local inference PoC

The standalone backend is usable on the tested Linux machine. **Oryn's graphical
chat remains the M0 demo**; there is no application adapter or durable chat yet.
See [ADR 0013](adr/0013-m1-local-llama-server-poc.md).

Follow-up: [Coder candidate and sequential two-model experiment](m1-candidate-models.md).
Gemma is prepared as a candidate but its official download is awaiting account access.

## Setup and run

From the repository root, requires Python 3.12+ (safe tar extraction), curl,
Linux x86_64 and a working NVIDIA driver. The tested machine uses Python 3.13,
Debian 13, driver 550.163.01 and an RTX 3060 12 GiB. CUDA toolkit installation is
not required by these downloaded runtime binaries. Other configurations are
not validated. The agent's sandbox cannot access this GPU; the actual smoke ran
with authorized access outside it.

```sh
python3 scripts/inference/setup.py
python3 scripts/inference/run.py
```

Setup downloads approximately 5.8 GB, verifies official SHA-256 metadata pinned
in the manifest, and extracts into `.local/inference`. Existing valid files are
reused; incomplete downloads can resume. If a file fails verification, move it
aside before retrying. No service is installed. No root permission is required
for these repository-local files. To recheck an existing installation offline:

```sh
python3 scripts/inference/setup.py --verify-only
```

Stop any server launched through `run.py` before setup or verification. Both
commands take a file lock: setup refuses to overwrite libraries used by a live
server. This prevents truncating memory-mapped shared libraries during inference.
The lock was verified against a real running server.

The server uses localhost port 18080, one slot, a 4096-token context, F16 KV cache,
full requested GPU offload, thinking off, and the embedded Jinja template. Its
web UI is disabled. Model and runtime files are ignored by Git. Stop with Ctrl+C.
For a single synthetic local request from a second terminal:

```sh
curl --no-buffer http://127.0.0.1:18080/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{"model":"oryn-core","messages":[{"role":"user","content":"Combien font 17 + 25 ?"}],"stream":true,"max_tokens":64}'
```

## Reproduce measurements

Close the manually launched server first. The smoke owns a temporary server and
stops it even if a check fails. It rejects an already-serving HTTP port. Reports
contain synthetic prompts and responses; server logs may contain prompt data, so
keep production conversations out of these diagnostics.

```sh
python3 scripts/inference/smoke.py
python3 scripts/inference/smoke.py --flash on --output artifacts/m1/flash-4096.json
python3 scripts/inference/smoke.py --flash on --context 8192 --output artifacts/m1/flash-8192.json
```

The reports and associated `.server.log` files are under ignored `artifacts/m1`.
Reports preserve exact model/backend settings, raw server token timings, content
latency, stream completion versus token-limit truncation, and GPU samples before
loading, after loading, after requests and after shutdown. These are **samples,
not peak VRAM measurements**. Startup-to-ready includes warmup and benefits from
the operating system's file cache; it is not a cold-disk loading benchmark.

## Observed results, 2026-10-15

Qwen3 8B Q4_K_M, backend b11658 CUDA 12.8, driver 550.163.01, Ryzen 5 5500,
RTX 3060 12288 MiB, 16 GB-class RAM. Each row is one smoke run, not a statistical
benchmark. Both longer requests generate exactly 160 tokens with no prompt-cache
reuse. All 37/37 layers are on GPU. KV cache stays F16 in all three runs.

| Setting | Ready (s) | First content, two prompts (s) | Decode, two prompts (tokens/s) | Total GPU after requests (MiB) |
|---|---:|---|---|---:|
| 4096 context, Flash off | 1.30 | 0.113 / 0.330 | 51.87 / 51.91 | 6016 |
| 4096 context, Flash on | 1.31 | 0.049 / 0.048 | 54.08 / 54.37 | 5798 |
| 8192 context, Flash on | 1.30 | 0.051 / 0.048 | 54.04 / 54.30 | 6377 |

All three pass progressive output, explicit end-of-stream, client disconnect
followed by server idle (observed within approximately 0.10 s with 0.10 s polling),
stable generation counter, successful request after cancellation, HTTP 400 for
malformed input, graceful shutdown and unavailability afterward. The GPU returns
to approximately 626 MiB total use after shutdown. Startup logs confirm CUDA and
full layer offload. No maximum-context prompt or simultaneous Oryn rendering was
tested. Flash results are promising but need longer/repeated workloads before a
general performance claim.

For continued manual experiments, an 8192 context with Flash enabled fits here:

```sh
python3 scripts/inference/run.py --flash on --context 8192
```

The scripts intentionally retain the conservative 8B baseline defaults for
reproducibility. Higher-precision 8B remains untested; the 14B candidate was tested
below. Available VRAM must also cover context, computation and the graphical
desktop, not just the weight file. Current runs explicitly disable the server's
host prompt snapshot cache (`--cache-ram 0`); these historical initial runs used
its default. Prompt reuse is disabled for benchmark requests in both cases.

## Qwen3 14B comparison

The user requested exploring Qwen3 14B Q4_K_M before application integration.
The official Apache-2.0 GGUF is 9,001,752,960 bytes, revision and SHA-256 pinned in
`manifest.json` under `candidates.14b-q4`. It is downloaded and verified locally;
the 8B remains available and remains the scripts' default.

```sh
python3 scripts/inference/setup.py --model 14b-q4
python3 scripts/inference/setup.py --model 14b-q4 --verify-only
python3 scripts/inference/smoke.py --model .local/inference/models/Qwen3-14B-Q4_K_M.gguf --flash on --context 4096 --output artifacts/m1/14b-4096.json
python3 scripts/inference/smoke.py --model .local/inference/models/Qwen3-14B-Q4_K_M.gguf --flash on --context 8192 --extended --output artifacts/m1/14b-extended-8192.json
python3 scripts/inference/smoke.py --flash on --context 8192 --extended --output artifacts/m1/8b-extended-8192.json
```

Same hardware/backend as above, Flash Attention on, F16 KV cache, one slot,
temperature 0.7 / top-p 0.8 / top-k 20 / min-p 0 / presence penalty 1.5 and seed 42.
The host prompt snapshot cache is disabled. These are single runs, not averages.
The 14B offloads 41/41 layers; 8B offloads 37/37. Input embeddings still have a
CPU-mapped buffer; full layer offload is not a claim of zero host memory use.

| Model/context | Decode on two short prompts (tokens/s) | First visible content (s) | Sampled total GPU after requests (MiB) |
|---|---|---|---:|
| 8B Q4 / 8192 | 54.02 / 54.36 | 0.052 / 0.040 | 6520 |
| 14B Q4 / 4096 | 30.86 / 30.97 | 0.083 / 0.064 | 9730 |
| 14B Q4 / 8192 | 30.67 / 30.57 | 0.076 / 0.064 | 10389 |

The 14B at 8192 uses about 10.15 GiB of total GPU memory, leaving about 1.85 GiB
relative to the device's reported 12 GiB at this desktop load. This is adequate
for these runs, not a peak-memory or simultaneous SDL workload guarantee.
At 4096 its KV buffer is 640 MiB; at 8192 it is 1280 MiB. That reservation is
independent of whether a particular question is semantically easy or difficult.

The extended run feeds a synthetic document of **5580 prompt tokens**, with a
key in the middle; both models return the correct key in 8 output tokens with
zero cached prompt tokens. It also enables separate reasoning with a 256-token
budget, caps the entire generated response at 768 tokens, and asks a known-answer
arithmetic question. Only reasoning chunk/character counts are stored, not the
reasoning text. The same non-thinking sampling settings are retained to isolate
the comparison; this is not a search for optimal reasoning parameters.

| Workload, context 8192 | 8B Q4 | 14B Q4 |
|---|---:|---:|
| Long input: first visible content | 3.16 s | 5.80 s |
| Long input: total duration | 3.32 s | 6.05 s |
| Reasoning: first streamed event | 0.094 s | 0.157 s |
| Reasoning: first visible answer content | 4.85 s | 8.52 s |
| Reasoning: total duration | 8.16 s | 14.39 s |
| Total generated tokens, including reasoning | 439 | 441 |
| Arithmetic result | Correct (34) | Correct (34) |

The reasoning-mode latency shows why fast first-token results with thinking off
cannot predict time to a final answer on a harder workload. Memory samples stay
around their reserved level; token generation time increases instead.

Host memory is measured separately using Linux `/proc/PID/status`, in KiB:
after requests the 8B RSS is about 0.79 GiB and the 14B RSS about 0.88 GiB. Their
process high-water RSS is about 4.99 and 8.69 GiB respectively, including model
loading. These figures include file-backed mmap pages, not solely private memory;
OS file cache and total system RAM pressure must be distinguished from process
RSS. At the sampled end of each run the system still reports about 11.3 GiB
available RAM. This does not establish peak system RAM use.

Both final extended smokes pass the original streaming/cancellation/error/shutdown
checks and observe separate reasoning chunks. All servers are stopped afterward.
One earlier 8B extended run was interrupted with SIGBUS while setup re-extracted
live shared libraries. Its report/log remain as `8b-extended-8192-interrupted.*`.
The installation lock fixes that collision; the final comparison above uses the
successful rerun. This interrupted run is not included as a model result.

### Quality observations and interpretation

Two narrow engineering probes were also retained for human review:

- C# default interface methods: both models incorrectly say `new C().M()` compiles
  when `C` only implements `interface I { void M() { } }`. Expected: A=NON;B=OUI
  for direct versus interface-cast calls. A temporary .NET 10 compilation confirmed
  CS1061 for the direct call and successful compilation with only the cast call.
- Late asynchronous chunks after cancellation: neither response clearly supplies
  the required generation-identity check before applying updates to the current
  response. Suggested delays/locks/global flags are insufficient as stated for
  an uncooperative old provider after generation B starts. This is a manual
  assessment, not an automated semantic quality score.

The 14B is therefore **hardware-viable at 8192 context with this configuration**,
but these few prompts do not establish a quality advantage over 8B. Keep both as
experimental candidates; assess more representative project questions before
choosing a default. No 8B Q8, 14B Q5, 32B or 16384-context run is claimed.

To interact manually with the measured 14B configuration:

```sh
python3 scripts/inference/run.py --model .local/inference/models/Qwen3-14B-Q4_K_M.gguf --flash on --context 8192
```

`run.py --reasoning on --reasoning-budget 256` enables bounded thinking for manual
experiments; keep a request-level output cap as well. The default remains off.

## Limits and next slice

Short synthetic prompts establish transport and lifecycle behavior, not coding
quality or long-context reliability. The initial 160-token output limit deliberately
truncates the longer answers. A clean `[DONE]` does not mean an answer is complete
or correct. The initial answer about C# interfaces includes an inaccurate
generalization; generated technical explanations still require verification.

The cancellation check closes the SSE response/connection, waits for an idle slot,
checks that the token counter stays stable, then issues another request. This is
server-level cancellation evidence, not an implemented Oryn Stop action. No
backend-crash recovery, UI responsiveness or message durability is claimed yet.

Next: implement the cancellable provider contract and chat state in C#, wire the
existing global chat without blocking render/input, then persist and restore
conversations per project. Explicit file context and broader failure coverage
follow. M-Native, multi-agent routing and the real knowledge graph stay separate.

Before upgrading the model, compare a small fixed engineering evaluation set,
including factual C#, design trade-offs and supplied-file questions. Compare
latency, answer quality and memory at the intended context length. Spare VRAM
can support a higher-precision 8B or a larger quantized candidate; it is not
evidence by itself that either produces better usable answers.
