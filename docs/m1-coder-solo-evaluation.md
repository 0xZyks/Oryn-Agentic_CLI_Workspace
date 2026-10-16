# Individual Coder evaluation: executable correctness and constrained architecture

Protocol date: 2026-10-15. Model: official Qwen2.5-Coder-7B Instruct Q6_K,
revision `13fb94bfda8c8cf22497dc57b78f391a9acb426a`, SHA-256 recorded in the
inference manifest. No planner, second model, network API or repository mutation
is involved in answering the problems. This evaluates a candidate, not a finished
Oryn feature.

Later protocol review during the [GPT-OSS comparison](m1-gptoss-evaluation.md)
identified ambiguous wording in `merge-intervals` and `generation-events`.
The original answers and grading below are preserved. Excluding those two cases,
this 7B configuration scores 3/11, 4/11, 4/11 before and after generic repair.
Use that sensitivity view for the principal comparison; the other failures still
support the configuration's limitations. Clarified-contract tool experiments are
reported separately and do not retroactively change these results.

## Frozen protocol

The previous six-question toy test was insufficient to establish coding ability.
This evaluation fixes **13 problems before querying the model**:

| Category | Problems | Independent evidence |
|---|---|---|
| Implementation/repair | Mean with overflow and integer division; interval merge; lexical topological ordering; fragmented UTF-8 lines; chat generation event reduction; async forwarding/cancellation | Compilation and executable contract tests |
| Code diagnosis | Mutable dictionary key; return/finally; incomplete `Task.WhenAll`; cancelled semaphore wait with unconditional release | Hand-authored code run with .NET 10 to establish the answers |
| Constrained architecture | Bounded UI event pipeline; durable message/inference ordering and recovery; ambiguous entity identity and human decisions | Numeric limits, a partial-order validator and explicit invariants |

`scripts/inference/coder_cases.py` contains prompts, reference implementations,
negative controls and tests. **Only the prompt reaches the model**, never the
reference implementation or test source. Architecture grading checks compliance
with the supplied constraints, not subjective design elegance or general
architectural competence. Multiple valid queue capacities and transaction orders
are accepted. There is no single claimed ideal architecture for arbitrary systems.

Six reference implementations pass **343 checks** in total (113, 17, 134, 29, 39,
11 by problem). All six deliberately broken implementations fail; all compile,
so these controls establish behavior sensitivity beyond compiler acceptance.
The topological-order tests use exhaustive permutations on small graphs as an
oracle independent of the reference Kahn implementation. Mean tests use decimal
arithmetic and a fixed random seed; Unicode tests split the same byte sequence
at every boundary. Cancellation tests include a source that ignores the token,
pre-cancellation, propagation, source failure and disposal.

Final oracle version: `coder-solo-v2`. SHA-256 of the case and verifier source:
`5fb4308da51e7328dca79f2e476edbd139f77b0eb8e5de796a5a3c8951457389`.
The successful self-test is a prerequisite; the model run refuses to use a
self-test from a different oracle hash. The initial v1 was frozen before model
queries. During review, two unstated requirements were removed: cancellation may
end iteration normally or throw, and the identity prompt asks for a manual-link
boolean without requiring it to be true. The valid v2 references/negative controls
were rerun, then **the same saved answers** were regraded, without regeneration.
Unchanged tests were reused; cancellation code was executed again. Scores did not
change because the failing answers still violate explicit constraints. Initial
reports, feedback and v1 sources are retained. Like any hand-authored test set,
this does not prove correctness beyond the covered contracts and cases.

## Execution and limits

Same Linux/Ryzen 5 5500/RTX 3060 12 GiB/driver 550.163.01 as earlier M1 work.
llama.cpp b11658 CUDA 12.8, 8192-token context, F16 KV, Flash Attention on,
one generation slot, host prompt snapshot cache disabled. Three seeds: 42, 7,
123. Request sampling: temperature 0.2, top-p 0.95, top-k 40, min-p 0, presence
penalty 0. An explicit system role describes C#/.NET assistance; no separate
reasoning mode is claimed for this model.

Maximum output: 1800 tokens per implementation, 512 per diagnosis/architecture.
First attempts are scored separately. A failed implementation receives **one**
repair opportunity, using only its original request, previous answer and compiler
or failing-test messages. No reference solution or test source is provided in
feedback. Diagnosis/architecture answers are not retried with the expected answer.
Reports preserve raw answers, truncation, token usage, first-content latency,
generation duration and verification duration.

Generated source is inserted into a fixed .NET project with no model-controlled
packages, analyzers or build files. Compiled programs run under Linux Bubblewrap:
network/PID namespaces, no repository or personal directory, runtime and test
binaries read-only, private temporary directory, cleared environment. Time and
CPU limits bound execution; managed heap is capped at 256 MiB. This is benchmark
isolation on the tested Linux host, not a shipped Oryn sandbox capability.

Preflight found that a 1 MiB file-size limit prevented CoreCLR from loading its
own assemblies. The runner was corrected to allow 128 MiB before any model query;
the complete oracle self-test then passed. That infrastructure failure is not a
model result. Runtime output captured in reports is bounded. Other operating
systems and arbitrary hostile generated projects are not evaluated here.

## Reproduce

Requires the previously verified local Coder model, Python, .NET 10, Bubblewrap
and `prlimit`. The runtime isolation requires user namespaces, unavailable inside
the agent's outer sandbox; these runs used authorized execution outside it.

```sh
python3 scripts/inference/coder_bench.py --self-test --output artifacts/m1/coder-solo-reproduce
python3 scripts/inference/coder_bench.py --output artifacts/m1/coder-solo-reproduce
```

Historical outputs: `artifacts/m1/coder-solo/results.json`, its server log and
per-case source/compiler/runtime logs under `checks/`. Final corrected grading is
`results.regraded-v2.json`. The v2 self-test is under
`artifacts/m1/coder-solo-v2/self-test/oracles.json`; v1 source snapshots are under
`artifacts/m1/coder-solo/protocol-v1/`. All are ignored by Git. The model server
is stopped on completion or failure. No application code, model default or
project knowledge is changed.

## Results

### Main condition: French, three seeds

| Category | Seed 42 | Seed 7 | Seed 123 | After one compiler/test feedback |
|---|---:|---:|---:|---|
| Implementations, all contract tests passing | 2/6 | 2/6 | 2/6 | No additional problem solved |
| Code diagnosis | 1/4 | 2/4 | 2/4 | Not retried |
| Explicit architecture constraints | 1/3 | 1/3 | 1/3 | Not retried |
| Total problems | 4/13 | 5/13 | 5/13 | Same totals |

These are **13 unique problems repeated**, not 39 independent problems or a
representative population estimate. Compilation succeeds for 5/6 implementation
problems on each seed, but only 2/6 satisfy the tested behavior. No first answer
is truncated and there are no infrastructure/runtime-launch failures in the
model grading. For repair, the original v1 failure messages were used; the final
answers were then regraded with the less restrictive v2 requirements. Generic
test feedback sometimes identifies an invariant without a full concrete input,
so these results are not a complete interactive debugging evaluation.

| Problem | First-attempt passes across 3 seeds | Observation |
|---|---:|---|
| Mean/overflow | 3/3 | Empty input, division and extremes handled |
| Merge intervals | 3/3 | All supplied boundary/non-mutation checks pass |
| Lexical topological order | 0/3 | Uses FIFO; violates the required choice among currently available nodes |
| Fragmented UTF-8 lines | 0/3 | Calls a nonexistent 7-argument `Decoder.GetChars` overload, still present after compiler feedback |
| Generation events | 0/3 | Stop loses the saved partial response |
| Async forwarding | 0/3 | Emits a late item when the source ignores the token |
| Mutable key | 3/3 | Correct diagnosis |
| Return/finally | 0/3 | Predicts 2 instead of the compiler/runtime-confirmed 1 |
| `Task.WhenAll` | 2/3 | One answer incorrectly predicts early completion |
| Semaphore cancellation/finally | 0/3 | Misses `SemaphoreFullException` masking cancellation |
| Bounded UI pipeline | 0/3 | Exceeds memory limit, chooses dropping despite no-loss requirement, puts waiting producer on UI |
| Durable chat order | 3/3 | Required transaction/recovery constraints satisfied |
| Ambiguous identity | 0/3 | Guesses a replacement despite missing rename evidence |

On this small target set, the model handles some bounded algorithms but is
**not reliable enough as the sole code/architecture authority for Oryn**. In
particular, compiling is far from sufficient for cancellation, ordering and data
preservation. Passing the closed architecture item does not establish broad
design ability, and a failure here is a failure of an explicit invariant rather
than a subjective dislike of a design.

### Controls

The English follow-up was introduced after observing the French failures. It
uses manually translated contracts and a translated system policy, the same
oracles and sampling, but only seed 42. It passes **3/13 initially**, and **4/13**
after a repair of the mean implementation. It therefore does not rescue the
observed overall reliability. This is exploratory, not a three-seed bilingual
study. Its repair wrapper retains French phrasing, so the clean language
comparison is the **first-attempt** score only.

A second control repeats the seven French diagnosis/architecture requests with
Flash Attention **off**, seed 42. It passes **2/7**, exactly as the matching
Flash-on condition; raw outputs are identical for those seven requests. This
does not certify every backend/kernel, but provides no evidence that this setting
caused those failures. No alternative backend, quantization or model size was
tested in this benchmark.

```sh
python3 scripts/inference/coder_bench.py --language en --seeds 42 --output artifacts/m1/coder-solo-en-new --oracles artifacts/m1/coder-solo-v2/self-test/oracles.json
python3 scripts/inference/coder_bench.py --flash off --categories diagnosis architecture --seeds 42 --output artifacts/m1/coder-solo-flash-off-new --oracles artifacts/m1/coder-solo-v2/self-test/oracles.json
python3 scripts/inference/coder_regrade.py artifacts/m1/coder-solo/results.json artifacts/m1/coder-solo-en/results.json --snapshot artifacts/m1/coder-solo/protocol-v1/coder_cases.py --oracles artifacts/m1/coder-solo-v2/self-test/oracles.json
```

English reports are under `coder-solo-en`, corrected grading in
`results.regraded-v2.json`; Flash-off results are under `coder-solo-flash-off`.

A final **concrete-feedback control** revisits three failed implementations from
the original seed-42 first answers, not from the previously repaired answers.
Each receives one alternative repair opportunity with a specific failing input
or execution trace; reference implementations and the rest of the tests stay
hidden. Cancellation now passes all 11 checks, using normal iteration termination
after cancellation (valid under v2). Topological ordering and generation-event
reduction remain unchanged and fail their full contracts. Thus precise evidence
can help this model on a particular bug, but did not generally repair the set.
This selected control is **not** pooled into the first-attempt score or presented
as a three-seed repair rate.

```sh
python3 scripts/inference/coder_counterexamples.py
```

Report: `artifacts/m1/coder-solo-concrete-feedback/results.json`, with the exact
counterexamples, answers, compiler/tests and server lifecycle recorded.

### Efficiency

The main French batch takes **231.64 s** including model loading, 39 first
responses, 12 repair responses and compilation/execution. Model response time
totals **95.43 s initially + 63.69 s for repairs**. Generated tokens total
3982 initially + 2600 for repairs. Median server decode speed is approximately
**43.8 tokens/s**. Failed repair attempts consume substantial time without
improving this batch's contract-level score.

Successful mean and merge requests take a median **1.88 s / 4.13 s** to generate.
Failed topological ordering takes about **7.40 s** initially; UTF-8 about **5.20 s**.
These are generation durations, excluding independent verification. GPU samples
are 6799 MiB loaded / 6821 MiB after requests (about 6.66 GiB), not measured peak
memory. Process RSS after requests is 879816 KiB; high-water RSS 6430992 KiB
includes loading/mmap pages. No claim of cold-load latency or simultaneous SDL
render performance is made.

### Interpretation for Oryn

Retain this model only as a **candidate for narrow, verified suggestions**, not
as an independently trusted architect or author of critical streaming/storage
logic. Do not let these outputs approve design states or overwrite identities.
Any production choice needs more representative project-level evaluation and
mandatory deterministic validation. The benchmark supports rejecting the current
7B/Q6/configuration as the sole expert for this requirement; it does not establish
that every Qwen Coder model, language or tool-assisted workflow will fail.
