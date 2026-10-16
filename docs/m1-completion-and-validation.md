# M1 completion: project history, context, setup and user-run evaluation

Status updated 2026-10-16: points 1–4 now have a compiled implementation: owned backend lifecycle, journal/gzip history, bounded context, hardware setup and profiles. The detailed behavior and current limits are in [the implementation guide](m1-llama-cpp-integration.md). The user-run battery below has not been executed. Retrieval indexing, summaries, a general export/import workflow and full automatic benchmark telemetry remain future work; design targets below must not be mistaken for executed acceptance evidence.

## 1. Local project history and compression

Interpret the requested ID-based data file as a **separate local data repository**; no remote transmission or synchronization is implied. The implementation gives a project a persistent UUID, with a locator in `.oryn/project.json` and data under `ChatDataRoot/<uuid>/` (default `.local/oryn/projects/<uuid>/` in the project). The data root may be configured before initializing its locator; an existing root is not silently relocated. Chat, graph and decisions must use the same project identity and knowledge model as those are introduced.

Moving a workspace can retain its UUID; a Git clone without the ignored locator must not silently acquire another project's chat. Export/import should carry identity and sources explicitly, and handle duplicate IDs deliberately. Avoid deriving identity from an absolute path, project name or Git remote alone.

Storage design (journal, sealing and paging implemented; the retrieval index in step 5 remains future work):

1. Append messages and generation states to a recoverable active journal, with stable message IDs and explicit completed/interrupted status. Checkpoint a streamed reply periodically and on cancellation/clean shutdown; do not write a full history on every token.
2. Seal a segment at a measured byte/message threshold. Record its schema version, sequence/range, source IDs, codec, uncompressed size and integrity hash in a manifest.
3. Compress sealed segments without loss. Write a temporary archive, verify a decompression round-trip and integrity, then atomically publish its manifest entry. Retain the original until the archive is safely committed. Do not assume repeatedly recompressing an archive produces a useful gain.
4. At startup, load the manifest and recent active conversation, not the entire decompressed history. Page older segments only when the user navigates to them or retrieval needs an excerpt.
5. Later, index archived messages in the project's common retrieval catalog, retaining segment/message references. The index/cache is derived and rebuildable; it is not the only surviving copy of the conversation. Its implementation follows the persistence/retrieval milestones rather than creating a second opaque memory now.

Lossless compression saves disk space; semantic summaries save prompt tokens but can lose details. Keep original messages and human decisions authoritative. On corruption, preserve affected data, report the problem and recover unaffected segments; never replace an unreadable history with an empty history silently. A future SQLite catalog can index canonical segments, with migrations/export defined before replacing the current store.

## 2. A bounded model context

The archive is what Oryn retains. Context is the smaller selection sent for one generation. Use the selected model's actual context capacity and tokenizer, including the chat template and any tool schemas:

```text
system/template + current request + selected history + retrieved excerpts
    <= context capacity - output reservation - safety margin
```

Initially use the current request, explicit selected source excerpts and the most recent complete turns that fit. When older turns no longer fit, keep them on disk and tell the user that the full history was not sent. If the current request alone exceeds the available budget, ask to narrow it or select a suitable profile rather than truncating it silently. If exact tokenization is unavailable, identify conservative estimates as estimates and provide a backend-overflow fallback; character counts are not exact tokens.

Later retrieval can reintroduce relevant archived excerpts with source IDs. An optional rolling summary must be labeled as generated, cite its message range, preserve uncertainties and be regenerable from originals. Budget its own generation cost. Basic bounded history does not require implementing all of M6 now.

## 3. First-run setup and model suggestions

Implemented first-run flow, pending user validation: inspect hardware/backend support → show usable profile candidates → let the user select → obtain consent for the displayed model/license/download → run a short user-initiated local probe → retain measured settings and a fallback. Detect installed and available RAM, supported GPU/backend, available VRAM and free disk space; unified memory must not be counted twice. Detection failure means unknown, not zero resources.

Selection must include quantized weights, KV cache/context, runtime/OS headroom and latency. A 16 GiB CPU-only machine should start by evaluating a smaller quantized model class, such as the user's 4B candidate, rather than automatically choosing the workstation's GPT-OSS profile. Neither parameter count nor fitting weights alone guarantees an acceptable result. Measure a cold load and a short warm request, and allow smaller context/offload profiles, manual overrides, offline operation and setup cancellation.

Remember the difference between **no model** (navigation and saved data remain available) and **core-only** (one primary model handles all logical roles). The current Python launcher is Linux/CUDA-specific; portable settings do not make that runtime portable.

## 4. Prepared quality battery: run it through Oryn

The versioned suite is [`benchmarks/m1/cases.json`](../benchmarks/m1/cases.json). It includes C# behavior, grounded text extraction with revisions/negation, constrained reasoning, arithmetic and explicit ambiguity. Each case has a JSON output contract and a reviewed expected answer. It is a small **public development suite**, not a held-out benchmark or proof of general coding ability. The expected answers are deliberately excluded from exported prompts.

Preparation and grading use only Python's standard library and never send prompts to a model or execute generated code:

```sh
python3 scripts/benchmarks/m1.py prepare --config oryn.settings.json --output artifacts/m1/manual-run-01
```

On Windows use the appropriate Python command. Run from this repository, or provide absolute paths. The configuration must select an installed model. Relative model/runner paths are resolved against `--project-root` (default: the config file's directory), then copied as absolute paths into isolated scratch workspaces. The preparation output is private local test data, not a portable config to commit. It never alters the source workspace/history and refuses an existing output directory.

For each case, open its generated scratch workspace in Oryn, e.g. `oryn artifacts/m1/manual-run-01/workspaces/C01`, paste the corresponding `prompts/C01.txt`, and copy only Oryn's answer into `answers/C01.txt`. Each workspace begins with a fresh history. Do not rerun a case in an already used workspace when claiming a fresh-context run. Fill measured durations in `measurements.csv`; leave unavailable token/resource values blank. Label cold/warm and the actual model/profile explicitly; a label is not automated verification of the loaded model. The default condition is `unspecified`; use `--condition cold` or `--condition warm` only with a recorded protocol. A freshly started process does not prove a cold disk cache, and a server that finishes loading before submission does not measure startup-inclusive request latency. Record launch-to-ready separately.

Then grade the captured answers:

```sh
python3 scripts/benchmarks/m1.py grade --run artifacts/m1/manual-run-01
```

The report distinguishes missing answers, invalid JSON/contracts and wrong answers; its denominator includes every case. Timing/resource summaries retain model/condition groups, observation counts and failed-case observations. The grader measures neither token timing nor GPU usage itself. A strict JSON mismatch can be a format failure even when the underlying explanation was plausible; inspect the recorded reason. No suite/model run has been executed as part of preparation. For catalog profiles installed outside this checkout, pass `--assets-root /path/to/inference-assets` or configure `InferenceAssetsRoot`; legacy `InferenceModelPath` configs are also supported. Scratch histories are always isolated under the generated workspace.

For generated C# implementations with compiler/behavioral verification, the existing standalone [`coder_bench.py`](../scripts/inference/coder_bench.py) and [`coder_verify.py`](../scripts/inference/coder_verify.py) provide a separate reference. Inspect their CLI/runner requirements before execution. They run outside Oryn, use the Linux inference PoC and can execute model-produced code; they are not an Oryn UI benchmark or a substitute for reviewed, isolated code execution. The new manual JSON suite does not compile or grade generated implementations.

## 5. User-executed integration scenarios

Use disposable scratch projects and retain copies of their data for failure cases. These are acceptance targets, not claims that the current implementation passes.

| ID | Scenario | Verifiable outcome |
|---|---|---|
| I01 | Launch a project with its own settings; repeat with `--config`. | Project settings are discovered; explicit override takes precedence; relative paths use the opened root. |
| I02 | Generate, cancel, then send another request. | Partial reply remains identifiable; new reply receives no late fragments from the previous generation. |
| I03 | Cancel during model loading; close during loading and generation. | UI stays usable; all owned processes exit; interrupted state is recoverable. |
| I04 | Complete a chat, close and reopen the same project. | User and assistant messages return in order; no silent duplication or automatic resend. |
| I05 | Stop during a partial reply and reopen. | Saved partial content/status follows the documented checkpoint policy; original user message survives. |
| I06 | Make a scratch history unreadable/corrupt, then attempt a new exchange. | Original data remains available for recovery; an error is visible; no silent overwrite. |
| I07 | No configured model, missing GGUF, missing backend, occupied port. | Navigation still works; the cause is visible; Oryn never adopts/kills another server as if it owned it. |
| I08 | Exceed the context budget with a long conversation/request. | Context exclusion/overflow is explicit; archived history remains accessible. |
| I09 | Switch Fast/Core repeatedly, including cancellation. | Selected profile is visible; old process is released before a new large model is resident. Implemented, not run in this session. |
| I10 | Seal/archive a segment, interrupt archive publication, verify export/import. | Uncompressed content hashes match; no messages lost; partial writes recover. Journal/sealing implemented; general export/import still a future feature. |
| I11 | First-run CPU-only / GPU / unknown-device setup; decline a download. | Suggestions follow supported devices and measured fit; declining keeps Oryn usable. Implemented, not run in this session. |

Mark unimplemented scenarios **not available**, not passed or failed measurements. Mark implemented but untested scenarios **not run**.

## 6. Benchmark the complete system (point 7)

Use fixed suite versions and compare the same tasks/profile/context across runs. Record model file/hash and quantization, backend/build and arguments, Oryn revision/dirty state, OS/hardware, context/output budgets, sampling/reasoning settings and relevant background load. Separate Fast/Core and cold/warm conditions. Repeat each measured condition and retain failures and outliers; do not claim meaningful p95 from a handful of samples.

Measure two distinct paths: **backend-only inference** through the existing harness, and **Oryn end-to-end** from user submission to visible output/completed result. Include queueing, tokenization/context assembly, loading/switching, persistence and any verification/repair. Manual timing is coarse and must be labeled as such; automatic metrics remain an implementation task.

Report correctness/format failures and time/resource expenditure together: first visible response, completion time, cancellation delay, token usage where reported, measured memory samples with scope, CPU/GPU contention, startup time, archived size and history reopen time as those features become available. Unknown is blank/null, not zero. A whole-GPU sampled maximum is not isolated process VRAM or a guaranteed peak.

Evaluate whether compression reduces bytes and startup cost on real histories, whether selected context preserves required answers, and whether profile recommendations produce usable latency without memory failure. Later HIE A/B/C comparisons require their own controlled protocol; a standalone model score does not establish Oryn or HIE quality.
