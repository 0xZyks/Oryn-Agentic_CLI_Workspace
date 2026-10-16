# ADR 0013 — First M1 inference slice: local llama.cpp server

Date: 2026-10-15. Status: accepted for the first PoC, following the user's choice.

## Context

M0's global chat is still an in-memory `Hello World!` demo. M1 needs real local
streaming, cancellation, visible failures and persistent chat without coupling
application state to a particular inference engine. The primary development
machine has a Ryzen 5 5500, 16 GB-class RAM and an RTX 3060 with 12 GiB VRAM.

## Choice

Use **llama.cpp `llama-server` as a separate local process**, initially started
explicitly by the developer. Pin the backend and official Qwen GGUF revision and
verify their SHA-256 digests. Keep downloads under Git-ignored `.local/inference`.
No system service, driver replacement or Oryn-owned native inference library.

The initial artifact is Qwen3-8B Q4_K_M from `Qwen/Qwen3-8B-GGUF`, Apache-2.0.
The manifest in `scripts/inference/manifest.json` records exact artifacts.
Start with one model, one generation slot, 4096 context tokens, F16 KV cache and
thinking disabled. Use the GGUF's Jinja chat template. Benchmark Flash Attention
separately before choosing settings for the application adapter.

Bind explicitly to `127.0.0.1:18080`, use a local model path and `--offline`,
disable the web UI, and send only synthetic prompts in the smoke. The subsequent
C# adapter will consume HTTP/SSE behind a backend-independent asynchronous,
cancellable provider contract. None of that adapter or UI wiring is implemented
by this PoC. The HTTP API's compatibility does not imply use of an external API.

## Alternatives

- Ollama: viable management layer, initially suggested; user prefers direct
  llama.cpp and its inference controls for this experiment.
- In-process llama.cpp bindings: possible later, but native lifetimes and ABI
  coupling are unnecessary for proving the first real stream.
- Custom inference runtime/kernels: outside v0.1 scope.

## Consequences

The developer currently owns server startup/shutdown. The PoC scripts target
Linux x86_64 CUDA only; Windows/macOS packages remain unverified. Backend and
model binaries do not belong in Git. The process boundary keeps native inference
failure separate from the future UI, but does not constitute a security sandbox.
The first measured Q4 model is a baseline, not a permanent model choice.

## Verification

`python3 scripts/inference/setup.py --verify-only` verified all three digests and
extracted the binaries. The pinned CUDA 12.8 build ran successfully with driver
550.163.01 without changing the system CUDA toolkit or driver. All 37/37 model
layers were offloaded to the GPU, as observed in the server log.

Follow-up comparison at the user's request: the official Qwen3 14B Q4_K_M
candidate is pinned separately and passes the same smoke at 4096 and 8192 context,
with 41/41 layers offloaded. A real long-input and bounded-reasoning comparison
also ran on 8B and 14B; results and quality limitations are in the PoC guide.
This does not promote 14B to the application default. Setup and launch now share
an installation lock after a setup/inference collision interrupted an early run;
host prompt snapshot caching is explicitly disabled for controlled comparisons.

Further experiment: Qwen2.5-Coder-7B Q6 is verified and tested with a sequential
8B-preparer/Coder pair; see the candidate comparison guide. This does not select
a multi-model default or implement M5. Gemma 3 12B is only pinned/prepared: its
official download is gated and returned HTTP 401, pending user account access.
The subsequent individual Coder benchmark finds insufficient contract-level
reliability for selecting that 7B/Q6 configuration as Oryn's sole code/architecture
expert; the transport choice does not imply model-quality acceptance.

`scripts/inference/smoke.py` measures readiness, time to first content, server
token timings and sampled total GPU memory. It verifies progressive SSE, normal
stream termination, disconnect cancellation followed by idle/stable generation
counter, a subsequent request, HTTP 400 on malformed input and clean shutdown.
See [the PoC guide](../m1-inference-poc.md) for commands, measured results and
remaining acceptance work. This does not establish M1 completion or model quality.

## Sources

- [llama.cpp server documentation, pinned b11658](https://github.com/ggml-org/llama.cpp/blob/b11658/tools/server/README.md)
- [Official backend release](https://github.com/ggml-org/llama.cpp/releases/tag/b11658)
- [Official Qwen model card, pinned revision](https://huggingface.co/Qwen/Qwen3-8B-GGUF/blob/7c41481f57cb95916b40956ab2f0b139b296d974/README.md)
