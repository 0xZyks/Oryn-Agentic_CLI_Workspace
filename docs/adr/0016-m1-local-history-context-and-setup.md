# ADR 0016 — M1 owned inference, local history and hardware setup

- Status: implemented in the working tree; compilation verified, user-run behavioral validation pending.
- Date: 2026-10-16.

## Context

The first chat integration worked for the user but lacked bounded context, safe server ownership and recoverable partial history. The user requested implementation of points 1–4: lifecycle, compressed project history, context budgets and first-launch hardware/model setup, while reserving test/benchmark execution for themselves.

## Decisions

- Keep state and orchestration in App, independent of SDL. Use one backend gate, bounded token delivery, immutable generation IDs and request/startup deadlines. Retain a compatible explicit Python runner while supporting native `llama-server` discovery/installation.
- Reject occupied ports; establish protected readiness with a random per-process alias and an ephemeral environment-provided API key. Never attach to or kill a foreign server. The server and application client use IPv4 loopback only.
- Use a project UUID locator in `.oryn/project.json`, with a single-writer transactional JSONL journal and verified immutable gzip segments in configurable `ChatDataRoot/<uuid>`. Default data is repository-local `.local/oryn/projects`, matching the requested development layout. Keep legacy JSON untouched, checkpoint streaming replies, label interrupted recovery and refuse writes after detected damage. Page old messages rather than decompressing the whole archive at startup.
- Reserve output/template/safety space, count with the active model's template/tokenizer and retain complete recent pairs. Missing endpoints permit an explicitly labeled conservative estimate; other count errors refuse inference. Persist explicitly attached preview excerpts with path/hash/time, labeled as non-revalidated snapshots. No full RAG, lossy summarization or separate opaque knowledge store.
- Hardware setup suggests profiles using available memory and supported runtime targets, while exposing uncertainty. Download/install only after explicit UI confirmation with size/origin/license information, verify pinned hashes and publish immutable runtime directories. A short local probe is a separate user action; it does not certify broad model quality or memory fit. Model profiles use catalog IDs for portable project settings where possible.

## Alternatives and limits

2026-10-16 correction: the user's GPT-OSS startup failed with CUDA out of memory during KV allocation. The integration had omitted the two CPU expert layers from the measured hybrid profile, and its legacy Python path defaulted to Flash Attention off. Expose validated `CpuMoeLayers` and `FlashAttention` fields and forward them to both launchers. Linux/NVIDIA GPT-OSS suggestions use two CPU expert layers and Flash Attention on; the current project config is explicitly updated while existing external profiles remain unchanged. Keep the 4096-token context, F16 cache and weight quantization; no automatic unmeasured fallback. Actual in-Oryn fit remains user-run validation.

Keeping the whole JSON history and sending every message was simpler but unbounded. Recompressing the entire archive at each save or eagerly decompressing it at startup would not meet the requested lazy-loading goal. SQLite and the project knowledge catalog remain the M2 integration direction; the current segment manifest is a narrow chat implementation, not a competing graph database. General export/import and retrieval indexing remain future work.

Bundled runtime targets are Linux x64 CPU/CUDA 12.8, Windows x64 CPU and macOS ARM64/Metal. Availability of pinned packages is not evidence of native execution on every platform. CPU/VRAM suggestions are estimates, and unknown memory stays unknown. Setup can use smaller 4B-class candidates, but Qwen3-4B quality/latency has not been benchmarked here.

## Sources and verification

The [pinned b11658 server documentation](https://github.com/ggml-org/llama.cpp/blob/b11658/tools/server/README.md) defines template/tokenization endpoints and API-key behavior; health alone is public and insufficient to prove ownership. Hashes/sizes are pinned from [the b11658 release](https://github.com/ggml-org/llama.cpp/releases/tag/b11658) and [the official Qwen3-4B GGUF metadata](https://huggingface.co/Qwen/Qwen3-4B-GGUF/tree/bc640142c66e1fdd12af0bd68f40445458f3869b). No model/archive download was performed by the agent.

The complete solution, including prepared tests, compiles outside the restricted sandbox with zero warnings/errors. This resolves the previously silent CLI build failure in that environment. Tests, native UI smoke, setup downloads, actual model probes and benchmarks have not been executed during implementation, per the user's instruction. Prepared checks cover migration, archive paging/integrity, incomplete/corrupt journal handling, writer exclusion, token budgets, no-model drafts, port ownership and deterministic UI streaming through an injected fixture.
