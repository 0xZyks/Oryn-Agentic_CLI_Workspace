# M1 — Larger coding models with hybrid CPU/GPU inference

This follows the [GPT-OSS-20B evaluation](m1-gptoss-evaluation.md). It is a local
evaluation fixture, not Oryn's production agent runtime or completion of M1.

## Model and rationale

Qwen3-Coder-30B-A3B-Instruct has 30.5B total parameters, 3.3B active per token,
48 layers and 128 experts, with 8 experts selected. The original model is
non-thinking. Sparse activation saves computation; it does not eliminate the
memory needed to store all expert weights.
[Source model](https://huggingface.co/Qwen/Qwen3-Coder-30B-A3B-Instruct).

Pinned conversion:

- Producer: `unsloth/Qwen3-Coder-30B-A3B-Instruct-GGUF` (community conversion,
  not a Qwen-published GGUF).
- Revision `b17cb02dd882d5b6ab62fc777ad2995f19668350`.
- `Qwen3-Coder-30B-A3B-Instruct-Q4_K_M.gguf`, 18,556,689,568 bytes.
- SHA-256 `fadc3e5f8d42bf7e894a785b05082e47daee4df26680389817e2093056f088ad`.
- Apache-2.0; the existing pinned llama.cpp b11658 CUDA 12.8 is reused.

The file is larger than VRAM, but a hybrid configuration can keep some experts
in host RAM and attention/KV on GPU. The experiment retains Q4 weights instead
of first forcing a more aggressive quantization to fit every weight on GPU.
Memory-mapped files remain enabled; a whole-model private RAM copy is avoided.

The native llama.cpp expert cache is also evaluated. For small batches, it runs
host-expert operations on GPU, uploads misses and retains experts in an LRU cache.
Large batches use the ordinary offload path. It costs additional VRAM and is not
a guarantee of improved speed. No custom kernel, fork, system service or driver
change is introduced.
[Upstream implementation](https://github.com/ggml-org/llama.cpp/pull/29887).

## Protocol and reproduction

The same `coder-solo-v2` oracle sources, reference proof and 13 tasks are reused
without changes. Temperature 0.7, top-p 0.8, top-k 20, repetition penalty 1.05,
presence penalty 0, min-p 0; the first four sampling settings follow Qwen's model
card. One slot, Flash Attention, F16 KV, batch 512 and microbatch 128. No separate
reasoning is enabled. Output limits and offload settings are recorded in reports.

```sh
python3 scripts/inference/setup.py --model qwen3coder30b-q4
python3 scripts/inference/gptoss_probe.py --model qwen3coder30b-q4 --context 8192 --cpu-moe 26 --long-input --output artifacts/m1/qwen30b-baseline.json
python3 scripts/inference/gptoss_probe.py --model qwen3coder30b-q4 --context 8192 --cpu-moe 30 --moe-cache-mib 1536 --long-input --output artifacts/m1/qwen30b-expert-cache.json
```

Despite its historical filename, `gptoss_probe.py` accepts both candidates and
uses the selected model's declared reasoning and sampling profile. It tests
streaming, disconnect cancellation, idle, another request, malformed input and
optional tokenizer-sized long-input retrieval. The Max-method transport probe
is not counted as an implementation-quality result.

Memory extrema are sampled after readiness, not guaranteed peaks. Process HWM
also includes loading/file-backed pages. System-wide swap movement is not
automatically attributable to this server; per-process VmSwap is also recorded.

## Scope of the native-tool probe

`agent_probe.py` exposes only three tools: read `Solution.cs`, replace that file,
and compile/run independent tests. The generated runtime uses the existing
Bubblewrap isolation, fixed project and bounded execution. There is no arbitrary
shell, network, package installation or access to the real repository.

The model starts from predefined failing fixture methods and can take at most
eight turns and four test calls. This is a selected single-file task experiment,
not a general multi-file coding-agent benchmark. For the event reducer, the
contract explicitly clarifies that Stop keeps the current text, with a concrete
example. Merge explicitly specifies closed intervals on the real line, shared
endpoints and a contrasting adjacent-integer example. These clarifications
address ambiguities found while reviewing the original prompts; the standalone
oracles and raw scores are preserved, and the principal standalone comparison
also reports an 11-task view excluding both cases. That additional context means the tool result cannot be pooled with the
unchanged standalone problem or attributed to tools alone. Native tool messages
are preserved between calls; reasoning text stays in memory and is not logged.

`read_file` also exposes a read-only `ContractExamples.md` fixture with public
examples (including invalid UTF-8 and cancellation boundaries). These are
additional context, not the hidden reference implementation. The model cannot
edit this file or the independent verifier. The final protocol enables KV-prefix
reuse and stops immediately when the current file passes the verifier; it reuses
that successful result instead of redundantly running it again. This is host
control of a bounded experiment, not the model autonomously accepting a change.

The initial exploratory `artifacts/m1/qwen30b-agent/results.json` used no prefix
reuse and allowed the model to continue after passing tests. It repeatedly called
the same successful tests for Merge (four calls, eight turns). That run was
interrupted during UTF-8 to improve the controller; its server still exited 0.
The incomplete exploratory run is preserved and excluded from the final tool
results. The final run uses a separate `qwen30b-agent-verified` directory.

## Observed hardware fit and optimization

Both configurations successfully load, stream, cancel to idle, answer a new
request, reject malformed HTTP input and stop with exit 0. Both retrieve the key
correctly from 5576 input tokens with zero prompt-cache reuse. The selected model
file is downloaded and SHA-256 verified; no more aggressive weight quantization
was needed to obtain a working configuration.

| Setting | Ordinary hybrid | Native expert cache |
|---|---:|---:|
| CPU expert layers | 26 | 30 |
| GPU expert-cache MiB | 0 | 1536 |
| GPU weight buffer | 8506 MiB | see raw server log |
| Initial probe sampled total GPU maximum | 10594 MiB | 10742 MiB |
| Host process RSS after long-input probe | 9.13 GiB | 10.55 GiB |
| Process high-water RSS including loading | 12.73 GiB | 12.46 GiB |
| 5576-token input → first visible token | 27.82 s | 28.32 s |
| Two subsequent fixed 512-token generations | 32.64 / 34.46 tokens/s | 24.90 / 25.67 tokens/s |

The throughput comparison uses the same long-form prompt, sampling, model file,
context and two 512-token ceilings after the transport probes. These output
ceilings intentionally truncate the prose and are **not correctness failures**.
The cache comparison trades four additional CPU expert layers for 1536 MiB of
GPU cache; it is a practical comparison at similar GPU occupancy, not a causal
experiment holding the entire offload configuration constant. There are only two
long generation samples per setting. The tested cache configuration is slower;
the result does not prove every expert-cache configuration is slower.

The first baseline arithmetic request experiences 5.01 s to first content and
17.06 tokens/s, while later requests improve as mapped CPU weights warm. Thus
warm decode throughput is not a cold-start guarantee. Initial readiness is
20.86 s baseline / 13.05 s cache on these runs, with different file-cache states;
the difference is not evidence of intrinsically faster cache loading.

Most host RSS consists of mapped weight pages. Linux MemAvailable includes
reclaimable file pages and cannot be treated as additional independent free RAM
on top of this model's working set. Process VmSwap is observed (initial probes
maximum approximately 143 MiB baseline / 49 MiB cache); host pressure and other
applications can change performance. Both the model's resident pages and the
operating system's page cache matter. No claim of swap-free execution is made.

The **26 CPU expert layers, no expert cache, 8192 context** configuration is
selected for the quality batch. Relevant raw reports:
`artifacts/m1/qwen30b-baseline.json`, `qwen30b-expert-cache.json`,
`qwen30b-warm-baseline.json`, `qwen30b-warm-cache.json` and adjacent server logs.

```sh
python3 scripts/inference/run.py --model .local/inference/models/Qwen3-Coder-30B-A3B-Instruct-Q4_K_M.gguf --context 8192 --flash on --cpu-moe 26
python3 scripts/inference/coder_bench.py --model qwen3coder30b-q4 --model-sampling --context 8192 --cpu-moe 26 --reasoning off --code-limit 4096 --json-limit 1024 --seeds 42 7 123 --oracles artifacts/m1/gptoss20b/self-test/oracles.json --output artifacts/m1/qwen30b
```

## Standalone quality results

| Same 13 problems | Seed 42 | Seed 7 | Seed 123 |
|---|---:|---:|---:|
| Implementations, first / after repair (of 6) | 3 / 3 | 3 / 3 | 2 / 3 |
| Diagnostics (of 4) | 2 | 2 | 2 |
| Closed architecture constraints (of 3) | 2 | 2 | 2 |
| Total first (of 13) | 7 | 7 | 6 |
| Total after allowed repair (of 13) | 7 | 7 | 7 |
| Excluding the two ambiguous cases, first and after repair | 6/11 | 6/11 | 5/11 |

The only additional passing answer from ten generic implementation repairs is
the ambiguous event-reducer case in seed 123. It is not counted as a firm gain
in the 11-task view. UTF-8 and asynchronous forwarding remain incorrect, and the
third seed also fails lexical topological order. The WhenAll completion state,
semaphore/finally exception and bounded pipeline constraints remain incorrect.
The larger parameter count does not establish better reasoning on these tasks.

All outputs complete without truncation. The batch takes 431.17 seconds including
generation, build/tests and server ownership. First answers generate 5394 tokens
in 219.57 seconds; ten repairs generate 2618 tokens in 121.27 seconds. Median
server decode is 36.33 tokens/s, median first visible content 1.58 seconds and
median complete first response 3.18 seconds across this mixed short-task set.
Those latency figures reflect very different output/reasoning lengths from
GPT-OSS and should not be treated as equal-work throughput comparisons.

Maximum sampled total GPU use is 10603 MiB and maximum sampled process RSS is
9.60 GiB after readiness. Maximum sampled process VmSwap is approximately 47 MiB.
Server exits 0 without forced shutdown. The measured working configuration is
feasible; its standalone correctness trails GPT-OSS's 9/11 after repair. This is
not a production acceptance result for either model.
Raw report: `artifacts/m1/qwen30b/results.json`; grading proof reused from
`artifacts/m1/gptoss20b/self-test/oracles.json` with the unchanged oracle hash.

## Qwen native-tool results

The completed controlled run fixes **2/4 selected fixture tasks**:

| Fixture | Native inference turns | Test calls | Final result |
|---|---:|---:|---|
| Merge with clarified shared-endpoint semantics | 4 | 1 | Pass |
| UTF-8 fragments | 8 | 3 | Fail: invalid/incomplete bytes do not throw |
| Event reducer with clarified Stop semantics | 6 | 2 | Pass |
| Async forwarding | 8 | 3 | Fail: late item and disposal |

Prefix reuse is actually observed in API cached-token counts, not just requested.
The host stops after the first successful verification; failed cases exhaust
the eight-turn bound. The entire run takes 154.59 seconds, server exit 0.
This is not a controlled estimate of improvement over standalone generation:
it starts from predefined broken code, provides public examples and clarifies
two contracts. It demonstrates native tool use and exposes remaining semantic
failures even with those advantages. Raw report:
`artifacts/m1/qwen30b-agent-verified/results.json`.

## Devstral Small 2 evaluation

Because Qwen's measured standalone and tool results remain inadequate, the next
screen uses the user-authorized alternative Devstral Small 2 24B:

- Producer `unsloth/Devstral-Small-2-24B-Instruct-2512-GGUF`.
- Revision `6e458b8add42681bfd023de5eab93637694aaf82`.
- `Devstral-Small-2-24B-Instruct-2512-Q4_K_M.gguf`, 14,334,446,752 bytes.
- SHA-256 `d14ba9edee1bb4c4996a726deb81e49ae81800a3216f0774634238c380aee496`.
- Apache-2.0, text inference only; no vision projector is downloaded or tested.

The downloaded file is SHA-256 verified. It loads with 28/41 layers on GPU,
8192 context and F16 KV. A second profile places 16 dense FFN blocks on CPU,
declares all layers for GPU offload and uses Q8 KV. The backend still maps some
tensors on CPU. Both profiles fit, but neither is responsive on this machine:

| Profile | GPU model buffer | GPU KV | Sampled total GPU maximum | Short decode | Long input |
|---|---:|---:|---:|---:|---:|
| 28/41 GPU layers, F16 KV | 9128 MiB | 864 MiB | 11321 MiB | 5.99–6.06 tokens/s | 6122 prompt tokens, first content 31.56 s |
| 16 CPU FFN blocks, Q8 KV | 8652 MiB | 680 MiB | 10536 MiB | 5.85–5.91 tokens/s | 6122 prompt tokens, first content 31.56 s |

Both pass the arithmetic/streaming probe, cancellation, follow-up request,
malformed-request rejection and clean server exit. The hidden key is retrieved
from the long input. The second profile reduces sampled GPU use by about 0.8 GiB
without a measured speed gain; because CPU FFN placement and KV precision both
changed, this is a configuration comparison rather than an isolated ablation.
Maximum sampled process RSS after readiness is about 4.9–5.3 GiB; process HWM
reaches about 12 GiB during loading. Per-process swap is sampled up to about
133 MiB. System-wide swap movement is not attributed to this server.

The seed-42 standalone screen uses the same 13 tasks and oracle hash as the
Qwen/GPT-OSS work, model sampling, 8192 context, 4096 code-token ceiling, 1024
JSON-token ceiling and one deterministic repair for failed implementations.
The completed batch takes 921.70 seconds. It scores **5/13 first response and
5/13 after repair**. All six implementations fail both attempts (0/6), while
three of four diagnoses and two of three architecture answers pass. Excluding
the ambiguous merge and event descriptions, the sensitivity score is 5/11;
none of the four remaining implementation cases passes. This is substantially
below GPT-OSS at 9/11 after repair and Qwen3-Coder-30B at 6/11, although the
model profiles and repeats differ.

The same four selected single-file cases are then run with the native tools and
bounded host loop described above. Devstral passes Merge, UTF-8 fragments and
async forwarding, but exhausts eight turns and three test calls on generation
events. The focused tool run takes 1041.18 seconds and exits cleanly. It shows
that tests and targeted repair can recover several failures from the solo
screen, including UTF-8, at a high interaction cost. It does not make Devstral
a practical default here: the measured decode rate is around 6 tokens/s, the
four-case run takes over 17 minutes, and event handling still fails. GPT-OSS
passes a different 3/4 subset in 183.19 seconds; its UTF-8 case hits the 4096
completion-token ceiling before calling a tool. A targeted GPT-OSS low-effort
retry is recorded in the GPT-OSS report. These small selected runs are not
pooled scores or general coding-agent evaluations.

Raw reports: `artifacts/m1/devstral-baseline-valid.json`,
`artifacts/m1/devstral-ffn-q8.json`, `artifacts/m1/devstral/results.json` and
`artifacts/m1/devstral-agent/results.json`. A first startup-log UTF-8 decoding
error prevented a request in `devstral-baseline.json`; it was a harness error,
not a model failure. The log reader was made tolerant of non-UTF-8 vocabulary
bytes, and the valid baseline was rerun.
[Source model and serving examples](https://huggingface.co/mistralai/Devstral-Small-2-24B-Instruct-2512).
