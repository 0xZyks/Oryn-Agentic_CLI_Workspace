# M1 local inference, project history and first-run setup

The native SDL chat uses llama.cpp on authenticated IPv4 loopback. Model loading, tokenization, generation, history loading, compression and installation run outside the render thread. No project/chat content is sent to a remote model.

The system context distinguishes Oryn's assistant identity from the active model: for example, “Je suis Oryn, propulsé par GPT-OSS-20B.” The runtime supplies the launched GGUF filename, independently of its random API alias. Recognized catalog filenames receive a readable model name; custom/renamed files with unknown identity are reported as unidentified rather than guessed from an unrelated catalog ID. Current identity is included in both context token counting and generation, and supersedes earlier model self-descriptions after a switch. This is application-provided identity metadata, not a guarantee that a model will always repeat it correctly or cryptographic verification of manually supplied weights. The loaded-model indicator remains the application reference.

## Start and configure

Build the current application, then run it from a project:

```sh
dotnet build Oryn.slnx --no-restore
dotnet run --project src/Oryn.Cli --no-build --
```

Without a selected model, the first launch opens the inference setup. Reopen it with `--setup` or **Agents / Settings → Inference setup / models**. `oryn --hardware` reports detected hardware and suggestions as JSON without opening a window, downloading or loading a model. GPU discovery currently recognizes NVIDIA through `nvidia-smi`; Apple Silicon is treated as unified memory, with unknown available memory left unknown. CPU-only and unknown-device cases remain usable.

In the setup, Up/Down, Tab or a click on a row chooses a candidate; **Enter or the bottom Apply action** saves and loads it. Reopening setup selects the currently configured catalog model. Choosing a row alone does not apply it. The panel distinguishes the selected candidate from the loaded model, displays the configuration path and effective memory settings, and shows loading/failure/ready state. Reselecting the same catalog model preserves its existing context, explicit model path and memory tuning; a different model starts from its suggestion. An old backend-startup error is cleared only when that backend has subsequently loaded successfully, without clearing unrelated chat/storage errors.

T runs an explicit local probe, and N continues without installing. For missing assets, a second Enter confirms download after displaying model size, origin and license plus backend details. Enlarge the window to at least 28 logical rows to review those details before confirming. Escape cancels an active operation or closes the idle setup. The probe uses the same effective profile as Apply, including manual tuning, and records readiness, first visible text, total duration and backend-reported tokens under `.oryn/inference-probe.json`; it is one local observation, not a quality benchmark or a guaranteed memory fit. No probe runs automatically.

Suggestions include Qwen3-4B Q4_K_M for smaller CPU-oriented profiles, Qwen3-8B Q4_K_M and GPT-OSS-20B MXFP4. Available RAM/VRAM and rough headroom influence recommendations; users may override them. Qwen3-4B has not been benchmarked by this implementation. NVIDIA 12 GiB-class cards leave little margin for GPT-OSS; fitting or a successful tiny probe does not establish performance on long tasks.

Installed models and runtimes use `InferenceAssetsRoot`, if set, relative to the opened project root. Development builds reuse this checkout's existing `.local/inference` directory when present; otherwise assets go under the platform's local application-data `oryn/inference` folder. Profiles store catalog IDs instead of machine-specific absolute model paths when selected through setup.

## Backend and downloads

The existing explicit `InferenceRunnerPath` Python configuration remains supported. Newly installed backends can run **directly as native `llama-server`**, without Python. `LlamaServerPath` selects a manually installed executable. Otherwise Oryn discovers a pinned installed runtime, with the existing Linux development runner as a fallback.

The installer knows pinned b11658 release bundles for Linux x64 CPU/CUDA 12.8, Windows x64 CPU and macOS ARM64/Metal. These are available installation targets, **not native execution/packaging certifications** for every OS. Other architectures require a matching manually configured executable. Downloads start only after setup confirmation, verify pinned byte lengths/SHA256, and use staging directories. Existing mismatching model files are preserved and reported. Interrupted `.part` downloads can be retried; they restart rather than claim resumable transfer. Incomplete staging folders are preserved for inspection. The installer does not replace a published runtime in place.

The official pinned server protocol documents [tokenization/template formatting](https://github.com/ggml-org/llama.cpp/blob/b11658/tools/server/README.md#post-apply-template-apply-chat-template-to-a-conversation). Release assets come from the [b11658 release](https://github.com/ggml-org/llama.cpp/releases/tag/b11658), and model revisions/checksums are pinned in `scripts/inference/manifest.json`, including the official [Qwen3-4B GGUF repository](https://huggingface.co/Qwen/Qwen3-4B-GGUF). Model licenses and download origins are shown in setup; CUDA bundles also carry component licenses.

## Project settings and profiles

`oryn.settings.json` is automatically loaded from the opened project root. `--config` overrides it. Relative model, executable and runner paths resolve from that project root, independently of the launch directory. Existing `InferenceModelPath` configs become a single Core profile in memory; no migration rewrites that config until an explicit setup/profile save.

A workstation example after installing the catalog models:

```json
{
  "SelectedModelProfile": "fast",
  "ModelProfiles": {
    "fast": {
      "CatalogId": "8b-q4",
      "ContextTokens": 4096,
      "MaxOutputTokens": 1024,
      "GpuLayers": 99
    },
    "core": {
      "CatalogId": "gptoss20b-mxfp4",
      "ContextTokens": 4096,
      "MaxOutputTokens": 1536,
      "GpuLayers": 99,
      "CpuMoeLayers": 2,
      "FlashAttention": "on",
      "Reasoning": true,
      "ReasoningBudget": 256,
      "ReasoningEffort": "low",
      "Temperature": 1.0
    }
  },
  "InferenceEndpoint": "http://127.0.0.1:18080"
}
```

For a CPU-oriented profile, select `4b-q4` and `GpuLayers: 0`, then measure it. A profile may instead set `ModelPath` to an existing GGUF. The defaults are a 4096-token context, 1024 output tokens and a 128-token safety margin; these are configurable per profile. Use **Agents → Select Fast / Select Core** to switch configured profiles. Switching cancels active generation, releases the previous process and loads the new one under a single backend gate. Core-only means one primary model, while no-model mode means no inference; both preserve navigation and saved data.

`FlashAttention` accepts `auto` (default), `on` or `off`. `CpuMoeLayers` defaults to zero and places the first N layers' expert weights on the CPU for MoE models; it is distinct from moving whole transformer layers with `GpuLayers`. Both settings reach the native server and compatible Python launcher. CPU placement trades GPU memory for host memory and potentially lower speed; it does not change weight quantization or the F16 KV cache.

For GPT-OSS on Linux/NVIDIA, setup now explicitly selects Flash Attention on and two CPU expert layers, retaining the 4096-token context. This follows the [historically measured hybrid configuration](m1-gptoss-evaluation.md#observed-fit-and-transport), which placed approximately 809 MiB of expert weights on CPU. The original integration omitted that placement and the Python runner defaulted to Flash Attention off; the user observed a CUDA out-of-memory error when allocating the KV cache. The project config is corrected. A later inspection observed an existing GPT-OSS process with both flags and a healthy public endpoint; that observation alone does not establish successful chat generation. VRAM occupied by the desktop and other applications varies, so this is not a fit guarantee. Existing profiles are not silently rewritten by the app: add these fields to an existing GPT-OSS CUDA profile. Reselecting that same profile preserves its tuning. If allocation still fails, the error suggests freeing VRAM or adjusting explicit CPU/GPU placement; Oryn does not silently retry with a different model or placement.

Startup and generation have separate configurable timeout limits (`StartupTimeoutSeconds`, `GenerationTimeoutSeconds`); the generation limit bounds the whole submitted operation, including a cold load. A protected random alias and ephemeral authentication key identify Oryn's own process. Occupied ports are rejected rather than adopting a foreign service. Only the owned child process is terminated. Output is streamed through a bounded UI channel, with immutable generation IDs and explicit cancellation checks before accepting chunks.

Chat messages use the full available text width with pixel-measured wrapping, and user prompts have separators above and below. Messages render **Markdown**: headings, bold/italic/combined emphasis, strikethrough, inline code, fenced/indented code with language header, nested lists, task lists, quotes, rules, links and pipe tables. Tables wrap their cells and stack them when the window is too narrow. An unfinished code fence remains visible while streaming. HTML is displayed as literal text; images use alt text, links are styled without automatic opening, and no remote assets or code are executed. Code has a separate background, without a syntax-coloring engine.

A small vector **copy icon below each nonempty message** copies its complete original Markdown, including any received partial response. The icon beneath an error copies the full diagnostic. Hover reveals its label; the status bar reports clipboard success or failure. Scroll to the bottom of a long message to find its icon.

Drag over message text to select an excerpt, then press **Ctrl+C**. Selection may span messages and copies **the displayed text**, omitting hidden Markdown markers/link destinations, decoding entities and separating table cells with tabs. Unicode and literal code content are retained, with two newlines between messages. Visual wrapping does not insert extra copied newlines. **Ctrl+A** selects the loaded message history while history has focus; **Tab** or clicking the composer returns to editing the preserved draft. Escape clears a selection when it is not already handling inference cancellation. Selection is read-only, remains attached to message IDs/source spans through paging/reflow/streaming, and dragging beyond the history viewport scrolls it. These interactions also apply to element discussions. The error diagnostic retains whole-text icon copying; substring selection concerns message bodies. Messages above 262144 characters fall back to literal text. Original history and inference input are not rewritten by rendering.

For visual review without running the test suite, opening SDL or loading a model:

```sh
dotnet run --project tests/Oryn.Ui.Smoke --no-build --no-restore -- --chat-preview artifacts/m1/chat-ui-preview
dotnet run --project tests/Oryn.Ui.Smoke --no-build --no-restore -- --markdown-preview artifacts/m1/markdown-ui-preview
```

This render-only mode uses fictitious messages and creates wide, compact, selected-text and high-density PNGs. It does not certify native mouse/clipboard behavior. The UI changes compile and these render-only captures have been visually inspected; prepared unit and interactive smoke scenarios remain for user execution.

See [ADR 0017](adr/0017-native-chat-markdown.md) for the parser/source-span/copy contract.

If `llama-server` exits during startup, Oryn waits for redirected output to drain before displaying its exit code and the last bounded diagnostic lines. The ephemeral API key is masked. Copy that error to diagnose startup failures; a successful build alone does not validate model startup.

## Durable project history

The locator `.oryn/project.json` contains a stable project UUID and the chosen data root. Default canonical data lives under `.local/oryn/projects/<uuid>/` inside the project; set `ChatDataRoot` before creating the locator to choose another local repository. Changing a data-root setting alone is rejected for an existing locator: move/import its files deliberately. A moved workspace retaining both locator and data retains its identity; a Git clone without ignored local data does not inherit another project's history.

The store keeps an active transactional JSONL journal, a versioned manifest and immutable gzip segments with hashes of their uncompressed contents. It checkpoints a partial reply approximately once a second when output arrives and at completion/cancellation/shutdown. A forceful stop can lose output since the last checkpoint; recovered pending replies are labeled Interrupted and are not resent. Journal compaction, sealing and manifest publication use atomic replacement; duplicate archived journal records are filtered by sequence after a crash. An incomplete tail is backed up before recovery; interior corruption blocks writes. One writer lease protects each project store.

`ChatRecentMessages` defaults to 40 and `ChatSegmentBytes` to 262144. Older completed prefixes are compressed and verified before publication. Startup loads recent messages, paging older segments only as needed. **Project → Load older project chat** retrieves another page; scroll the chat history to view it. Corrupt archives are retained and further writes are blocked once a read detects corruption. Integrity hashes detect damage, not malicious modification of both manifest and archive.

The former `.oryn/global-chat.json` is imported once, preserving its original bytes. The current automatic legacy import limit is 8 MiB; oversized or unreadable legacy data is preserved and reported, not silently discarded. Keep the original file plus the new locator/data when backing up a migrated workspace. No general export/import UI is claimed yet.

## Context budget and explicit sources

Before inference, Oryn applies the selected model's chat template and tokenizes the resulting prompt. It reserves output and safety budgets, then retains the newest complete user/assistant pairs that fit. The latest request and its explicit sources are never silently truncated; an oversized request is refused with an explanation. If token-counting endpoints are absent, the UI explicitly labels a conservative UTF-8-based estimate. Other counting failures stop submission instead of pretending to know an exact count.

Older turns stay on disk. Their exclusion count and token-count status appear in chat. Interrupted/failed/truncated assistant replies are not treated as completed historical answers. **Project → Toggle selected source context** opts in to attaching the current source preview to the next submitted request only. The bounded excerpt, path, hash and attachment time are persisted with that user message; it is labeled as a preview snapshot that has not been revalidated against disk. This does not claim full repository access, semantic analysis or RAG. Archive indexing and generated semantic summaries remain later work.

## Validation prepared for the user

The solution and prepared test projects compile. Unit tests and SDL/inference/model benchmarks were not run during this change, following the user's request to execute the tests personally. No model or backend archive was downloaded by the coding agent; only small public metadata was consulted to pin the installer catalog.

After building, execute as appropriate:

```sh
dotnet test tests/Oryn.App.Tests --no-build --no-restore
dotnet run --project tests/Oryn.Ui.Smoke --no-build --no-restore -- artifacts/m1/ui-captures
```

The deterministic UI fixture uses an injected stub provider; passing it would not prove real model quality. The unit suite includes migration, journal recovery, archive integrity/paging, writer exclusion, context budgets, no-model behavior and port ownership. Run the real lifecycle/setup cases and the public answer suite from [the user-run protocol](m1-completion-and-validation.md) separately. First-launch downloads, local probes, actual shutdown/cancellation and Windows/macOS native behavior still require that execution evidence.
