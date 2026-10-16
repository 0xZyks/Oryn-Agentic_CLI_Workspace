# M1 — GPT-OSS-20B local evaluation

This experiment follows the unsuccessful Qwen2.5-Coder-7B Q6 evaluation. It
does not connect an inference provider to Oryn's graphical chat.

## Pinned model and backend

- `ggml-org/gpt-oss-20b-GGUF`, revision
  `ef9b12f2ff56c69cf32153a02784e7a3c88bf524`.
- `gpt-oss-20b-MXFP4.gguf`, 12,109,566,624 bytes; SHA-256
  `27cd6c432c7672cb812a92f611cf3ba7bbc35928262bb1e1253ff4ee6ae35901`.
- Apache-2.0; conversion published by the llama.cpp team and linked from their
  [deployment guide](https://github.com/ggml-org/llama.cpp/discussions/15396).
- Existing pinned llama.cpp b11658 CUDA 12.8 on RTX 3060 12 GiB / Ryzen 5 5500 /
  16 GB host RAM. Model files remain in ignored `.local/`.

The OpenAI deployment recipe recommends at least 16 GB VRAM or unified memory
for comfortable use, and allows slower CPU offload. Host RAM and VRAM on this
machine are separate pools. The backend's `--n-cpu-moe` keeps selected experts
on CPU while attention and other layers can remain on GPU.
[OpenAI recipe](https://developers.openai.com/cookbook/articles/gpt-oss/run-locally-ollama).

## Protocol

The existing `coder-solo-v2` cases and grading sources remain unchanged. The
reference solutions, compiling negative controls and diagnostic runtime proofs
were rerun successfully before querying GPT-OSS. This is the same 13 unique
problems as the [Coder evaluation](m1-coder-solo-evaluation.md), not an unseen
general benchmark or proof of production agent quality.

Native GGUF Jinja template, separate reasoning enabled, medium effort and no
artificial 256-token reasoning cutoff. Temperature 1, top-p 1, top-k 0, min-p 0,
presence penalty 0. The output ceiling includes reasoning and final answer.
One repair with deterministic feedback is allowed for failed implementations.
Tests and reference solutions are hidden from prompts.

Launch settings are explicit: batch 512, microbatch 128, Flash Attention on,
F16 KV, one slot, no host prompt cache. Earlier Coder runs used the old launcher
batch defaults; raw throughput is not a controlled model-only comparison.
Memory instrumentation samples whole-GPU use and host memory every two seconds
after readiness; extrema are sampled values, not guaranteed peaks. Swap counters
are system-wide, so any movement cannot automatically be attributed to inference.

## Reproduction

```sh
python3 scripts/inference/setup.py --model gptoss20b-mxfp4
python3 scripts/inference/coder_bench.py --self-test --output artifacts/m1/gptoss20b
python3 scripts/inference/gptoss_probe.py --context 4096 --cpu-moe 0 --output artifacts/m1/gptoss20b-fit0.json
python3 scripts/inference/gptoss_probe.py --context 8192 --cpu-moe 2 --output artifacts/m1/gptoss20b-8192-cpu2.json
python3 scripts/inference/coder_bench.py --model gptoss20b-mxfp4 --model-sampling --context 8192 --cpu-moe 2 --reasoning on --reasoning-effort medium --reasoning-budget -1 --code-limit 6144 --json-limit 4096 --seeds 42 7 123 --output artifacts/m1/gptoss20b
```

GPU device access and Bubblewrap namespaces must be available to the process.
Every experiment owns and shuts down its local server; setup and inference
retain the existing exclusive/shared installation locks.

## Observed fit and transport

| Configuration | GPU model buffer | Sampled total GPU use | Decode, short probes |
|---|---:|---:|---:|
| 4096 context, zero CPU expert layers | 10,949 MiB | 11,881 MiB maximum sample | 72.6–73.3 tokens/s |
| 8192 context, two CPU expert layers | 10,140 MiB | 11,307 MiB maximum sample | 59.8–65.8 tokens/s |

Both configurations report 25/25 layers offloaded, which does **not** imply every
tensor resides on GPU: input embeddings remain CPU-mapped, and the second run
additionally places approximately 809 MiB of expert weights on CPU. The smaller
batch/microbatch produces a 23.6 MiB GPU compute buffer in this backend, far below
the older guide's reference configuration. The successful load is an observation
on this backend, not a contradiction of the guide's broader memory recommendation.

The first arithmetic answer is correct in both runs. Separate reasoning is
observed. First visible answer takes 2.56 s / 2.90 s for that short arithmetic
probe; decode speed includes reasoning tokens. Streaming, cancellation by
disconnect, idle within approximately 0.1 s, a subsequent request, malformed HTTP
400 and graceful exit 0 pass in both probes. GPU use returns to about 0.65 GiB.

Host RSS after requests is 0.37 GiB / 2.00 GiB, while process high-water RSS is
11.00 GiB / 11.64 GiB including model loading and file-backed pages. System-wide
swap counters move during both probes despite substantial available RAM; these
runs do not establish swap-free behavior or exclusive attribution of the movement.
The 8192 configuration is selected for evaluation to retain more GPU headroom.

## Larger coding candidates

The follow-up measurements are documented in the [larger coder evaluation](m1-large-coder-evaluation.md): both Qwen3-Coder-30B and Devstral Small 2 load locally, but their solo scores and tool-agent runs differ substantially. Those results replace the earlier “not measured” status below.

| Candidate conversion | Q4_K_M file bytes | Measured approach |
|---|---:|---|
| Unsloth Qwen3-Coder-30B-A3B-Instruct | 18,556,689,568 | Hybrid expert offload, 8192 context; 6/11 unambiguous standalone score after repair. |
| Unsloth Devstral-Small-2-24B-Instruct-2512 | 14,334,446,752 | Partial dense offload, 8192 context; 5/11 unambiguous standalone score after repair. |

Qwen's source model has 30.5B total / 3.3B active parameters and is non-thinking;
the model is not a 3.3B memory footprint. Keep Q4 quality as the first experiment
rather than immediately degrading weights to force GPU-only residency.
Devstral's smaller file does not establish lower latency under CPU offload; its
measured hybrid decode remains near 6 tokens/s. Neither result establishes
production acceptance.
[Qwen source model](https://huggingface.co/Qwen/Qwen3-Coder-30B-A3B-Instruct),
[Qwen conversion](https://huggingface.co/unsloth/Qwen3-Coder-30B-A3B-Instruct-GGUF),
[Devstral source model](https://huggingface.co/mistralai/Devstral-Small-2-24B-Instruct-2512),
[Devstral conversion](https://huggingface.co/unsloth/Devstral-Small-2-24B-Instruct-2512-GGUF).

## Completed standalone quality batch

| Same 13 problems | Seed 42 | Seed 7 | Seed 123 |
|---|---:|---:|---:|
| Implementations, first / after one repair (of 6) | 5 / 5 | 4 / 5 | 3 / 4 |
| Diagnostics (of 4) | 3 | 3 | 3 |
| Closed architecture constraints (of 3) | 2 | 2 | 2 |
| Total first response (of 13) | 10 | 9 | 8 |
| Total after allowed repair (of 13) | 10 | 10 | 9 |

These are three repeats of 13 unique tasks, not 39 independent questions. The
previous 7B/Q6 configuration achieved 4/13, 5/13, 5/13 before and after its
generic repair. Sampling, model size, architecture, quantization and reasoning
policy differ: this is a comparison of usable configurations, not an isolated
causal experiment on one variable. Neither model is accepted for production.

Mean, lexical topological order and asynchronous cancellation pass in all seeds.
UTF-8 passes two initial answers; the third is repaired after a real decoder
failure. Merge passes once initially, is repaired once, and remains incorrect
after repair in the third seed. All six initial implementations compile in all
three seeds, illustrating why compilation alone is insufficient.

Persistent failures:

- The event reducer returns empty text after Stop, including after generic
  feedback. The original prompt explicitly requests preserving partial text but
  also says to return the last "active" generation, which can be read as still
  running. Keep this limitation visible: a clarified-contract agent experiment
  must be reported separately and cannot silently improve this table.
- `await Task.WhenAll` is incorrectly reported as throwing `AggregateException`
  in all three seeds; the runtime proof confirms `InvalidOperationException`.
- The architecture answer selects `drop_oldest` despite the explicit no-loss
  requirement in all three seeds. The remaining constraints in that case pass.

Six implementation repairs produce two additional successful answers. No output
is truncated. The batch takes 1222.00 seconds including generation, compilation,
isolated tests and server ownership. Initial answers generate 51,145 tokens in
870.70 seconds; repairs generate 16,204 tokens in 284.23 seconds. Median server
decode is 60.16 tokens/s, but median **first visible final-answer content is
15.92 seconds**, and median whole initial response is 19.06 seconds. Reasoning
tokens make the high decode rate an incomplete measure of responsiveness.

Maximum sampled total GPU use reaches 11,699 MiB during this longer batch;
background GPU use rises from 664 to 996 MiB between server starts/stops. Thus
the short-probe free-memory margin is not a permanent guarantee. Maximum sampled
process RSS is 2.03 GiB after readiness; system available memory stays above
9.56 GiB in the samples. Swap counters are system-wide and move during the batch.
Server exit is 0 without forced shutdown. Raw report:
`artifacts/m1/gptoss20b/results.json`; oracle proof:
`artifacts/m1/gptoss20b/self-test/oracles.json`.

### Protocol sensitivity: exclude two ambiguous descriptions

On review, the merge prompt's "touching intervals" also permits a different
interpretation for integer intervals: the grader treats them as closed intervals
on the real line (shared endpoints merge), while the failed responses also merge
adjacent integers. That choice is not expressly disambiguated in the original
prompt. Together with the event reducer's "active" wording, this makes two cases
unsuitable for a firm model-quality rejection based on these scores alone.

**No grading source, original prompt, raw answer or original score is changed.**
A separate sensitivity view excludes `merge-intervals` and `generation-events`:

| Remaining 11 unique tasks | Seed 42 | Seed 7 | Seed 123 |
|---|---:|---:|---:|
| GPT-OSS first response | 9/11 | 9/11 | 8/11 |
| GPT-OSS after allowed repair | 9/11 | 9/11 | 9/11 |
| Historical Coder 7B first and after repair | 3/11 | 4/11 | 4/11 |

Use this view for the principal comparison. The remaining two repeated GPT-OSS
failures (WhenAll exception type and the no-loss overflow policy) are explicit
and remain supported by the runtime/constraint proofs. The selected tool probe
clarifies both ambiguous contracts and is exploratory, not a revised pooled score.

## Native-tool loop and reasoning-effort check

The four selected single-file fixtures use the same bounded tools, public
examples, clarified Merge/event contracts and immediate host stop on verified
success as the Qwen/Devstral experiment. At medium reasoning effort and a 4096
completion-token ceiling, GPT-OSS passes Merge, generation events and async
forwarding. UTF-8 consumes the full completion ceiling before making a tool
call, so that fixture fails without a test. The run takes 183.19 seconds and
exits 0; sampled GPU maximum is 11337 MiB. This is 3/4 selected fixtures, not a
general agent score.

A targeted UTF-8 rerun at low reasoning effort with the same 4096-token ceiling
does call tools, but still fails after two independent test runs: it does not
throw for invalid or incomplete UTF-8 and mishandles split/CRLF cases. It takes
85.79 seconds, with 7 turns and 2 test calls. Reducing reasoning effort removes
the output truncation but does not repair the implementation. Raw reports:
`artifacts/m1/gptoss20b-agent/results.json` and
`artifacts/m1/gptoss20b-agent-low-utf8/results.json`.

The probe script now records `--reasoning-effort` and `--max-tokens` per run so
this tradeoff can be reproduced. This is an evaluation harness, not yet Oryn's
production agent runtime.
