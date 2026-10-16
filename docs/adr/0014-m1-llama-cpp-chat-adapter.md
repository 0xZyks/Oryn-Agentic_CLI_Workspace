# ADR 0014 — M1 chat through a managed local llama.cpp server

- Status: accepted for the M1 integration slice.
- Date: 2026-10-15.

## Context

The SDL chat was a session-only preview while the repository already contained a pinned llama.cpp `llama-server` runner and measured GGUF candidates. Inference must not block SDL input or rendering, and model choice must remain replaceable as measurements evolve. Retrieval is a later milestone and should supply bounded, attributable excerpts without binding the provider to an index implementation.

## Decision

Oryn starts the existing `scripts/inference/run.py` runner as a child process only when `InferenceModelPath` is configured. The runner and model paths are configurable; the endpoint must be HTTP loopback. The App layer speaks OpenAI-compatible SSE through `IInferenceProvider`, receives streamed chunks on a worker, and applies UI state changes through the existing main-thread completion queue. Escape cancels the active generation. Oryn owns and terminates the child process when the workspace closes.

Global chat history is stored as readable JSON under the opened project's ignored `.oryn/` directory, with atomic replacement. A `ChatContext` input keeps retrieved source excerpts separate from chat history; it is empty until a later retrieval implementation.

## Alternatives

- Embed llama.cpp or add a native binding: rejected because the repository already has a tested server build and this would add FFI and platform-specific coupling before it is needed.
- Require a manually started server: rejected because model startup and shutdown would remain outside the workspace lifecycle.
- Add a database and RAG now: deferred; JSON meets the single global-chat M1 need and the README schedules retrieval later.

## Consequences and verification

The selected model is configured as one GGUF path; multiple large models are not resident together. The existing Python runner currently carries Linux CUDA PoC assumptions, so other platforms need a matching runner configuration. The backend's pinned build and standalone streaming/cancellation measurements are documented in `docs/m1-inference-poc.md`.

The App and SDL projects compile with zero warnings. End-to-end graphical inference was not exercised during this change; the CLI build currently exits during MSBuild reference resolution with no diagnostic output, and the environment is not being treated as proof of a successful UI session.
