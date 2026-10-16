# Oryn — Local-first AI Engineering Environment

> **Status:** Design specification / implementation roadmap for **v0.1**  
> **Audience:** Project author, contributors, and coding agents (including Codex)  
> **Principle:** *The AI assists the engineer's reasoning; it does not replace their agency.*

## 0. Read this first — instructions to Codex

This README is both the project vision and an actionable implementation brief. **Do not attempt to implement everything described here in v0.1.** Distinguish:

- **[V0.1]** Committed product scope: implement a functional, usable first release.
- **[LATER]** Direction for v1+; design extensibility where cheap, do not build prematurely.
- **[PROPOSED]** Candidate choice requiring benchmarking or a short proof of concept.
- **[DECIDED]** Product/architectural intent established during design.

**Development sessions:** inspect the existing solution and documentation, and preserve the working SDL3 + SkiaSharp prototype. Advance through the relevant milestone, including the planned M-Native interoperability foundation, without recreating the application. Present assumptions and short ADRs for decisions that cannot be inferred. Avoid broad rewrites, speculative frameworks, unbounded dependency additions, and features not listed as v0.1 deliverables. Never store credentials in the repository. Documentation requests do not authorize implementation of planned modules.

**Definition of success:** launching `oryn` opens a native graphical window and a project, browses files, chats with a local model, inspects a navigable project/concept graph, edits/validates proposals, opens contextual element discussions and knowledge cards, and restores that state on restart. Some graph edges, documentation, and agent proposals may be incomplete or manually supplied in v0.1; this must be made explicit rather than fabricated.

---

## 1. Why Oryn exists

Oryn is a **local-first, keyboard-first, terminal-inspired graphical engineering environment**, assisted by AI. Its interface preserves the interaction philosophy and information density of a TUI while using native graphics for rendering, layout, animation and visualization. Its original motivation is personal: restore the pleasure of programming, experimenting, designing systems and understanding technical concepts **without losing the practical benefits of modern AI assistance**. It is not intended to be merely another chatbot, autonomous coding agent, or code editor.

The central experience connects four activities:

1. **Discuss** an idea, design trade-off, implementation or unfamiliar technology with specialized AI agents.
2. **Visualize** both what exists in the repository and what is only being considered.
3. **Understand** each element through a contextual technical knowledge card, linked references and architectural rationale.
4. **Build and verify** the chosen design, preserving the history of why it was chosen.

The long-term ambition is to **develop Oryn using Oryn**: start with proven libraries and inference engines, then progressively implement an SDL + OpenGL renderer for v1.0, specialized scheduling, memory management and potentially parts of the inference stack. Future homelab hardware may host larger models while the graphical frontend remains local and functional offline.

### Guiding principles

- **Human-controlled architecture:** agents suggest; the user validates. AI proposals must never silently become approved decisions.
- **Code is factual; interpretations are not:** distinguish parser/analyzer evidence, AI-generated inference, and human-confirmed decisions.
- **Local-first, not local-only:** usable with local models and without network; optional remote inference through user-provided API keys.
- **Progressive enhancement:** file browsing and project knowledge should remain useful even if LSP, graph analysis, remote services or models are unavailable.
- **Responsive interaction:** window input, graph navigation and hover/click/keyboard actions must never wait for LLM inference.
- **Portable:** Windows, Linux, macOS; avoid hardcoding POSIX assumptions into shared application code.
- **Measurable optimization:** minimize unnecessary model calls and redundant analysis before attempting exotic low-level tuning.
- **No feature creep:** ideas beyond the agreed v0.1 scope go into the backlog / Idea Box.

---

## 2. Release boundaries

### 2.1 v0.1 — required product capabilities

1. **Graphical workspace:** launch from CLI into an SDL window, open a project directory, file explorer, top menu/command bar, central tabbed area, dockable or switchable views, keyboard-first navigation and mouse interaction. **[DECIDED] [V0.1]** SDL handles the window and events; SkiaSharp draws the interface. Terminal.Gui is superseded as the frontend choice.
2. **Project chat:** persisted conversations, streamed model output, references to project files/elements, explicit actions/tools with user-visible status and error handling.
3. **Multi-agent architecture:** actual distinct agent roles and task routing, with a basic scheduler, cancellation, priority classes and model backend abstraction. Agents may share a physical model.
4. **Interactive visualizer:** project/module/class/method/concept nodes, relationships, expandable semantic levels where supported, selection and navigation, context menus/actions, statuses and aggregated progress. Basic code-derived graph plus manually/agent-proposed nodes is sufficient initially.
5. **Element-level chat:** persistent discussions attached to stable element identities; reopen from the node; general chat and contextual chat are separate scopes but may reference one another.
6. **Knowledge cards:** purpose, mechanism, technology, design rationale, documentation/resources, relations, discussions and decision history; clearly label generated versus sourced versus user-confirmed information.
7. **Design lifecycle:** **À valider → Validé → En cours → Implémenté**; user-confirmed transitions; archived/rejected ideas in a searchable **Idea Box**.
8. **Persistence:** project graph, element identities, decisions, chats, status, citations/references, knowledge cards and settings survive restarts.
9. **At least one viable language integration:** begin with a narrow, tested implementation, ideally **C# / Roslyn** for richer semantics, with graceful degradation for unsupported languages. Do not claim complete cross-language CFG support.
10. **Usable local inference:** start with an established engine and realistic small quantized models; support a core-only mode for machines unable to run multiple models comfortably.
11. **Multi-language interoperability:** validate useful Rust workspace indexing and C process execution through versioned C ABI tables and managed services; see M-Native. This is required prototype scope, not an implemented capability.

The existing SDL3 + SkiaSharp frontend is the graphical foundation. The target presentation includes dedicated Chat, Code, Git and Visualizer views and a floating Terminal overlay; Git and Terminal are planned, not part of the current M0 executable.

### 2.2 Explicitly out of scope for v0.1

- Direct OpenGL renderer and a complete general-purpose widget toolkit; SDL + OpenGL is the v1.0 target.
- Homemade GPU allocator, VRAM scheduler, inference kernels or model runtime.
- Full language package manager/catalog and plugin marketplace.
- Exhaustive cross-language call graph, CFG, data-flow and precise dynamic dispatch resolution.
- Full autonomous implementation of graph proposals without approval.
- Distributed inference / homelab orchestration (preserve interface seams, not implementation).
- Advanced RAG infrastructure requiring a heavyweight vector database.
- Perfect code-entity identity reconciliation after arbitrary refactors.
- Maintaining a parallel terminal frontend as a v0.1 requirement.

These are **future ambitions, not abandoned ideas**.

---

## 3. UX and workspace design

### 3.1 Layout

```text
┌──────────────────────── Oryn ────────────────────────────────┐
│ File  Project  View  Agents  Tools  Settings                 │
├────────────────┬──────────────────────────┬──────────────────┤
│ File Explorer  │ Tabs: Chat | Code | ...  │ Visualizer       │
│                │                          │ (dock right)     │
│ src/           │ Global project chat      │                  │
│  ...           │ or selected document     │ graph / detail   │
│                │                          │                  │
├────────────────┴──────────────────────────┴──────────────────┤
│ Status: model · jobs · indexing · diagnostics · key hints    │
└──────────────────────────────────────────────────────────────┘
```

**Current M0 layout:** FileExplorer is left; the central tabs are Chat / Sources / Visualiser, with Chat active at startup. Sources is a read-only code preview. Settings offers accent/background color selection with a saved personal appearance profile. The explorer offers bounded asynchronous filename/text search and vector extension icons; text matches are not semantic analysis. The visualizer may also dock right or left. The diagram illustrates optional right docking, not the startup layout. Layout adapts to window dimensions and must preserve keyboard accessibility.

**Target presentation [DECIDED]:** Chat, Code, Git and Visualizer are primary views; Code and Git are dedicated views rather than permanently docked panels. A floating, toggleable Terminal overlay preserves its underlying session when hidden. The Visualizer can also be a docked contextual view. Compact graphical containers, thin separators, restrained effects and configurable keyboard navigation with mouse support preserve the terminal-inspired philosophy. Oryn is not intended to become a conventional IDE. The presentation manager owns visibility, focus and layout, not necessarily service lifetimes. Git and Terminal remain planned.

### 3.2 Interaction

- Keyboard-driven focus, selection, navigation, search and context actions; mouse click, wheel, hover, drag and resizing through SDL events.
- Selecting a graph node opens its details; a context action such as **Show Chat** opens the node's persistent discussion. Support shortcuts and discoverable actions; exact keymap remains configurable.
- Node-local chatboxes may appear as floating overlays/panels and persist independently of visibility. Avoid making every chat permanently visible.
- Project chat is global; node chat is scoped to a concept or code element. Allow linking messages/decisions across scopes without duplicating history.
- Show status, source, confidence and staleness. A proposed node must look different from a code-backed implemented node.
- Verify display scaling, Unicode text, keyboard layouts, text input and mouse behavior on each supported platform. Keep navigation accessible in small windows.

### 3.3 Visualizer and semantic zoom

The visualizer is **not merely a drawing of source files**. It is a navigable map of both the actual system and the intended system.

Potential abstraction levels:

`Project → Modules/Namespaces → Classes/Types → Methods/Functions → Internal flow`

- Zoom changes **semantic granularity and visible entities**, not only glyph size.
- Show dependencies, incoming/outgoing calls, event and data flow, and architectural relationships, each with evidence and confidence.
- Control **scope** (what part of the project) separately from **depth** (how much internal detail).
- At deepest level, provide **Call Graph** and **Control Flow Graph (CFG)** as distinct switchable views; future synchronized side-by-side comparison is desirable.
- **CFG is beta/limited in v0.1** if supported by the chosen analyzer. Do not generate authoritative CFG branches with an LLM.
- Support code-derived entities **and** conceptual nodes not yet in code, with visible provenance.
- For a class or module, summarize child progress (e.g., 5 implemented, 2 in progress, 1 to validate). Counts are not proof of correctness.

### 3.4 Four states and Idea Box

| State | Meaning | Transition policy |
|---|---|---|
| **À valider** | Hypothesis/proposal, not yet approved | Created manually or proposed by agent |
| **Validé** | Accepted design/requirement, not started | Explicit human validation |
| **En cours** | Implementation underway, incomplete | User-controlled, can be assisted by evidence |
| **Implémenté** | User considers element finished and functional | Explicit confirmation, preferably backed by tests/evidence |

The **Idea Box** is **not a fifth main state**: it archives rejected, deferred or superseded concepts, including the original discussion, rationale and links to affected elements. Archived concepts remain searchable and retrievable as historical context, and can be reconsidered later.

Important subtleties:

- Detecting a class/method in source does **not** prove it is complete or correct.
- If an implemented element is being redesigned, create a **linked change proposal** instead of rewriting history to pretend the existing implementation disappeared.
- Preserve a timestamped audit trail of decisions and transitions.
- Status and code-presence are distinct internal properties, even if the user-facing workflow exposes just four main statuses.

---

## 4. Project Knowledge Graph — the central model

**[DECIDED]** The visualizer and the agents must share **one unified project knowledge model**. Do not build a separate opaque visualizer database and an unrelated AI memory database.

### 4.1 Entity classes

At minimum:

- **Code entities:** repository, project, module, namespace, file, type, method/function, field/property, relevant symbol.
- **Conceptual entities:** technology (e.g., RAG), architectural component, planned feature, subsystem, hypothesis, design decision.
- **Knowledge entities:** documentation card, external resource, conversation, message, decision record, archived idea.
- **Edges:** contains, references, calls, depends-on, implements, proposed-to-implement, documents, discussed-in, supersedes, related-to, data-flow, event-flow, CFG transition.

Do not require every edge type in the first milestone. Design typed edges and metadata so new types can be introduced safely.

### 4.2 Identity and reconciliation

Assign a stable internal **UUID** to each discovered or created element. Chats, documentation, decisions and statuses reference UUIDs, **never just a file path or display name**.

**Crucial caveat:** UUID assignment alone does not solve continuity when source code is externally renamed, moved or substantially edited. The analyzer will rediscover symbols; the system must reconcile them.

Suggested matching evidence, strongest first when available:

1. Explicit rename/move notifications from an IDE/LSP or tracked user action.
2. Semantic symbol identity, signatures, enclosing scope and language-specific IDs.
3. Git rename/move information and source location history.
4. Normalized code-body fingerprints and structural fingerprints.
5. Reference/dependency neighborhood similarity.
6. File path and line/column proximity as weak supporting signals.

Design reconciliation as a **scored matching operation with confidence**, not a simplistic string equality check. If ambiguous, **do not silently reattach a conversation to the wrong symbol**: preserve the old entity, mark it unresolved/orphaned, and offer user-assisted linking. Track merge/split cases and provenance; do not discard history.

v0.1 can start with exact matches, simple rename heuristics and a manual re-link UI; richer reconciliation is later.

### 4.3 Evidence and provenance

Every machine-produced fact should carry:

- origin: parser, semantic analyzer, LSP, user, agent, imported documentation;
- source pointer (file/range/symbol, URL, conversation/message ID) where applicable;
- observed/generated timestamp and last validation;
- confidence / certainty where meaningful;
- stale/invalidated flag when relevant.

**Three knowledge categories must not be conflated:**

1. **Deterministic or analyzer-backed observations** (code facts).
2. **AI-generated explanations/inferences** (fallible, must be validated or visibly labeled).
3. **Human-confirmed architectural intent** (must never be silently overwritten by a rescan).

A parser or analyzer is the source of truth for its supported code facts; a model can enrich descriptions, labels, grouping and proposed relations, **not fabricate CFG branches or claim code is functional**.

### 4.4 Incremental invalidation

On a file change, compute impacted symbols/edges and mark derived cards, summaries, embeddings and graph regions stale as appropriate. Recompute affected results rather than reindexing the entire project. Build a dependency/invalidation mechanism incrementally; don't overengineer it in the first iteration.

---

## 5. Contextual knowledge cards

A graph node is also an entry point into learning and project history. For example, selecting **RAG Engine** should offer a detailed card explaining what RAG is and how it relates to Oryn.

Recommended card sections:

- **Overview:** what the element is and why it exists.
- **Mechanism:** how it works, concepts, flow, inputs/outputs and trade-offs.
- **Technology:** libraries, standards, algorithms, interfaces and versions when known.
- **Documentation:** official docs, articles, papers, source links; distinguish verified links from suggestions.
- **Project role:** how Oryn intends to use it and its relationships to other nodes.
- **Design decisions:** what was approved, why, alternatives considered and rejected.
- **Implementation:** code locations, status, tests/evidence and outstanding work.
- **Discussions:** persistent chats attached to the element.

**RAG example:** explain retrieval-augmented generation, chunking, embeddings, lexical/vector/hybrid retrieval, reranking, context assembly, freshness, citations and hallucination limitations. Do **not** suggest a vector database is mandatory: lexical or structural code search can be more appropriate.

v0.1 may combine curated/static templates, project annotations and AI-assisted generation. Documentation claims should have citations or clear uncertainty labels; AI-generated prose is not itself an authoritative source. Cards must remain useful offline when previously cached.

---

## 5a. Multi-language architecture [DECIDED]

Oryn adopts a modular C# / Rust / C architecture to validate meaningful interoperability during the prototype while preserving the existing application and avoiding unnecessary complexity. These five logical layers define ownership, not mandatory individual assemblies.

| Layer | Language | Responsibilities |
|---|---|---|
| Oryn.App | C# | Application lifecycle, UI, workspace, agents, Git, events and orchestration |
| Oryn.Interop | C# | Native loading, ABI validation, unmanaged function pointers, marshalling and native resource ownership |
| Oryn.Runtime | Rust | Workspace indexing, selected semantic analysis, background workloads and future specialized scheduling |
| Oryn.Native | C | Process execution, future PTY/ConPTY, operating-system integrations and future sandbox backends |
| OS / Native Dependencies | Platform-specific | Filesystem, process APIs, native graphics libraries and system isolation mechanisms |

### Dependency model

```text
Oryn.App (C#)
    |
    | Managed services
    v
Oryn.Interop (C#)
    |
    | Versioned C ABI / delegate* unmanaged[Cdecl]
    |
    +----> Oryn.Runtime (Rust)
    |          +-- Workspace indexing
    |          +-- Semantic analysis
    |          +-- Background processing
    |          +-- Future task scheduling
    |
    +----> Oryn.Native (C)
               +-- Process execution
               +-- PTY / ConPTY
               +-- Platform APIs
               +-- Future sandbox adapters
```

Native modules are independent by default. Introduce direct Rust-to-C dependencies only for a concrete shared capability. Prefer coarse-grained calls, batched results and bounded event streams over repeated trivial FFI calls.

### Architectural boundaries

- Oryn.App owns application state, presentation and orchestration.
- Oryn.Interop is the managed gateway to Oryn-owned native modules.
- Oryn.Runtime implements selected compute-oriented and analysis workloads.
- Oryn.Native provides targeted operating-system integrations.
- Native modules expose C-compatible interfaces; panels must not depend on native implementation details.
- Native modules must not manipulate application UI state or block the UI rendering thread.
- The event bus distributes meaningful state changes; it does not replace every direct service call.

### Graphics ownership [DECIDED]

SDL3 and SkiaSharp remain in the C# application under the logical responsibility of Oryn.App. Moving SDL3 to Rust is not planned. The existing physical `Oryn.Ui.Sdl` adapter remains responsible for graphics lifecycle and rendering, while `Oryn.App` state stays framework-independent. This logical ownership does not require merging projects or moving files. See section 8 for graphics boundaries and section 3 for presentation policy.

### Prototype objectives [V0.1]

Validate loading Rust and C libraries, resolving versioned API tables, invoking typed unmanaged function pointers, a useful Rust workspace indexer and a useful C process executor. Return structured results to managed services; verify native ownership, cleanup and missing/incompatible-library handling while preserving SDL3 + SkiaSharp behavior.

These capabilities are planned through M-Native. The current solution has no Oryn.Interop or Oryn-owned Rust/C modules. Do not introduce a custom native renderer, inference engine or advanced scheduler merely to demonstrate interoperability.

---

## 5b. Native interoperability and ABI contracts [DECIDED]

Oryn-owned Rust and C modules use explicit native function-pointer bindings. The preferred C# mechanism is `delegate* unmanaged[Cdecl]<...>`, with exports dynamically resolved through `NativeLibrary.Load(...)` and `NativeLibrary.GetExport(...)`. Avoid large collections of declarative DllImport wrappers for these APIs. Third-party libraries may retain their existing binding mechanisms.

### Versioned function tables

Each module owns an independently versioned API table exposed through a stable entry point. Illustrative contract:

```c
#include <stdint.h>

typedef struct oryn_runtime oryn_runtime_t;

typedef struct {
    uint32_t abi_version;
    uint32_t struct_size;

    oryn_runtime_t* (*create)(void);
    void (*destroy)(oryn_runtime_t*);
    int32_t (*scan_workspace)(
        oryn_runtime_t*,
        const char* workspace_path
    );
} oryn_runtime_api_v1;

int32_t oryn_runtime_get_api(
    uint32_t requested_version,
    oryn_runtime_api_v1* out_api
);
```

This is not a production header. The actual contract must define calling convention (including Windows compatibility), export visibility, string encoding, buffer ownership and error reporting. It must also define the caller's output-table capacity and compatible extension rules before writing to `out_api`; a returned `struct_size` alone cannot prevent an overwrite. Structured indexing results, progress and cancellation require further contract design. Rust implementations expose C-compatible functions through `extern "C"`.

### Managed binding layer

Oryn.Interop owns module discovery, dynamic loading, export resolution, ABI checks, table initialization, native handles, string/buffer marshalling, error translation, managed service wrappers and safe shutdown. Raw pointers must not escape into application panels.

### ABI safety rules

1. Public structures use fixed-size C-compatible fields with exactly matching managed/native layouts.
2. Calling conventions must be explicitly compatible.
3. Document pointer ownership; release allocations through their owning module.
4. Native handles remain opaque outside their module.
5. Function pointers and handles must not outlive the loaded library; finish operations and destroy resources before unloading.
6. Rust panics must not unwind across FFI.
7. Errors use explicit status codes or documented result structures.
8. Long operations support cancellation and bounded shutdown where applicable.
9. Managed callbacks require explicit threading and lifetime contracts.
10. Pin or copy managed memory when required by the contract.

### API evolution

Validate requested and returned ABI versions, structure size, required function pointers and supported capabilities before exposing a managed service. Optional functions may use compatible table extensions or new API versions, with explicit capacity negotiation. A shared native loader is desirable; a universal table containing every operation is not.

### Threading and callbacks

Native workloads must not block SDL events or rendering. Prefer asynchronous managed orchestration and bounded polling initially. Introduce bidirectional callbacks only when needed, with documented threading and lifetime contracts.

### Native failure handling

Missing or incompatible libraries produce meaningful diagnostics and disable the affected capability while retaining file navigation and cached knowledge. Unless explicitly isolated, native modules run in the application process: a C or Rust memory-safety failure can terminate it. ABI separation does not provide process isolation. Move selected workloads behind IPC if stronger fault containment becomes necessary.

---

## 6. Chat, agents and model orchestration

### 6.1 Conversation scopes

- **Global project chat:** architecture discussions, cross-cutting requests, project-level decisions.
- **Element chat:** anchored to a stable graph UUID, persists even when the panel is closed; linked to relevant source and card.
- **Cross-reference:** a global discussion can propose or update a specific graph node; record which message led to which proposal.

Agent actions that mutate knowledge (new nodes, links, documentation, statuses) should appear as **reviewable structured proposals**. The user can accept, edit, reject or archive. Never silently promote an AI-generated hypothesis to a validated architecture decision.

### 6.2 Logical agents versus physical models

**[DECIDED]** Oryn is multi-agent, but **multi-agent does not imply one loaded LLM per agent**. Several roles may use one physical model, different prompts, tools and context. This is vital on 16 GB RAM-class systems and limited VRAM.

Initial agent roles (implement the minimal useful subset, not a complex autonomous society):

| Role | Responsibility | Outputs |
|---|---|---|
| **Router/Coordinator** | Classify intent, choose tools/agent/model, manage dependencies | Typed task plan / dispatch |
| **Project Assistant** | User-facing reasoning, coding and architecture discussion | Streamed chat response with references |
| **Graph Curator** | Turn conversations into proposed nodes/edges/status changes | Validated structured proposals |
| **Knowledge/Research Agent** | Explain concepts, suggest and contextualize documentation | Sourced or labeled knowledge-card content |
| **Code Analysis Tooling** | Deterministic indexing, symbols, references, optional CFG | Evidence-backed graph updates; **not necessarily an LLM** |

Agent roles can be logical configurations of the same model. The runtime should distinguish **model selection**, **agent policy**, **tools** and **execution priority**.

### 6.3 Tentative local model lineup

**[PROPOSED — benchmark before freezing]**

- **Router:** Qwen3 **0.6B** class — short classification/routing and simple structured output.
- **Light:** Qwen3 **4B** class — small summaries, graph/card enrichment and cheap tasks.
- **Core:** Qwen3 **8B** class — main conversation, architecture, coding and difficult synthesis.

These are **starting candidates, not mandatory dependencies**. Check current model licenses, available quantizations, tokenizer/chat-template compatibility, actual context requirements, tool/JSON reliability, latency and memory use. A tiny router may cost more in load/switch overhead than it saves; compare with deterministic routing and core-only operation. Never assume all three models can reside simultaneously in RAM/VRAM.

**Core-only fallback** is required as an architectural mode; a practical single-model v0.1 deployment is acceptable. Lazy loading, queueing and model reuse should be possible without redesigning agents.

### 6.4 Inference engines and API providers

**[PROPOSED] v0.1:** prefer a proven local inference backend such as **llama.cpp** (direct integration or local server) or **Ollama** for a fast first implementation. On Apple Silicon, **MLX** may be evaluated later behind the same provider interface. **Do not build a new inference engine in v0.1.**

Define an interface conceptually equivalent to:

```csharp
public interface IInferenceProvider
{
    IAsyncEnumerable<InferenceChunk> GenerateAsync(
        InferenceRequest request,
        CancellationToken cancellationToken);
}
```

A provider reports capabilities: streaming, structured output, tool calling, context length, local/remote, token metrics and optional model residency information. The agent layer must not depend on Ollama-specific response types.

**External APIs (optional, not required for v0.1):** support future user-configured OpenAI-compatible endpoints and, where necessary, dedicated adapters for provider-specific APIs. Use **the user's own keys**; never bundle keys or require a paid service to open projects. Credentials must be read from environment variables or OS credential stores (preferred), not committed to config or logs. Redact secrets from traces and screenshots. Make remote transmission of project source **explicitly opt-in**, with clear indication of which provider receives what content; offline/local-only mode must prevent such transmission.

### 6.5 Tools, tasking and safety

- Tool invocations should be typed, validated, cancellable and logged at a useful level.
- File reads and code analysis can be allowed by project scope; shell execution and file writes need a visible permission policy and user approval appropriate to risk.
- Treat repository files, documentation and model output as **untrusted inputs**, not instructions that override the user's permissions.
- Give interactive chat higher priority than background knowledge enrichment; indexing and LLM tasks should not freeze the graphical interface.
- Do not run an LLM on hover, mouse movement, scrolling, graph pan or other routine UI interactions.
- Parse/validate agent-generated JSON or typed proposals before committing; reject malformed or unsupported output and continue with deterministic behavior.

---

## 7. Language integrations and graph analysis

### 7.1 Language-neutral intermediate representation

**[DECIDED]** Normalize adapter results into a **Unified Graph Model / IR** that supports code entities and relations while retaining language-specific metadata and provenance. Do not flatten away distinctions that matter for correctness.

Potential adapter/tool sources:

| Language | Candidate tools | Notes |
|---|---|---|
| **C#** | Roslyn, C# LSP | Strong semantic analysis; Roslyn offers CFG facilities for supported operations |
| **C / C++** | Clang AST, clangd, Tree-sitter | Preprocessor, templates, function pointers complicate exact call graphs |
| **Rust** | rust-analyzer, Tree-sitter | Semantic APIs/capabilities vary; macros and trait dispatch complicate analysis |
| **Python** | Pyright, Tree-sitter | Dynamic dispatch limits static certainty |
| **JavaScript / TypeScript** | TypeScript language services, Tree-sitter | Dynamic behavior and module resolution matter |
| **PHP** | PHP language server / parser, Tree-sitter | Select tools based on supported semantics |

Tree-sitter provides incremental syntax parsing, **not by itself a complete semantic call graph or CFG**. LSP supports symbols/navigation and sometimes call hierarchy, but capabilities vary by server. Accurate cross-language CFG, data flow, async edges, dynamic calls and macros are hard; represent inferred/partial edges honestly.

**v0.1:** select one language path with measurable behavior; start with code entities, contains/references/calls where trustworthy, then optional beta CFG. Provide generic file browsing and manually maintained concepts for other languages.

### 7.2 Future language catalog and package manager

**[LATER]** A LazyVim/Mason-inspired **Language Integrations Catalog** should let users install/configure LSPs, parsers, semantic analyzers and associated tools. Simple mode installs a recommended bundle; advanced mode allows precise selection. Plugins declare capabilities (`symbols`, `references`, `callGraph`, `controlFlow`, `dataFlow`, etc.) and requirements. UI degrades gracefully if missing.

The author **does not want to build a complete package manager in v0.1**. A future Oryn Package Manager may distribute broader extensions, but should leverage existing package distribution where sensible and validate versions/checksums. Treat plugins as potentially untrusted code with explicit permissions/isolation policies.

---

## 8. Graphical architecture and platform support

### 8.1 SDL + SkiaSharp for v0.1 [DECIDED]

Oryn moves from the Terminal.Gui prototype to a **native graphical window**. The `oryn` CLI remains the launch entrypoint, including opening the caller's project directory. The interface no longer depends on terminal cells, ANSI output or terminal keyboard protocols.

**[DECIDED] [V0.1]** Use **SDL + SkiaSharp**: SDL owns window creation, lifecycle and platform events; SkiaSharp draws the interface, text, code canvas and visualizer. Terminal.Gui is no longer the selected frontend. The M0 executable now uses SDL3 + SkiaSharp; the former terminal adapter remains historical source outside the active solution. See [ADR 0006](docs/adr/0006-sdl3-skia-raster-m0.md) for implementation choices and verification limits.

The current M0 adapter pins SDL3 bindings and SkiaSharp versions and uses a raster drawing surface presented through SDL; see ADR 0006. Text/font handling and native packaging need further platform validation. A GPU-backed Skia drawing surface remains an alternative to measure, not the current implementation.

### 8.2 Critical abstraction boundary

**Keep application state independent of SDL, SkiaSharp and the future OpenGL renderer.** Preserve framework-independent concepts for:

- workspace state, panel/tab layout and docking;
- focus, selection, commands, keybindings and event routing;
- graph viewport and semantic navigation;
- overlays, contextual chats and detail cards;
- rendering contracts and invalidation;
- window dimensions, display scaling and platform capabilities.

Adapt the current cell-based geometry to graphical coordinates without introducing graphics-library types into the application model. SDL events map to application commands; file access, inference and indexing run outside the UI loop. Replacing the renderer must preserve chats, graph identities and interaction behavior. A drawing abstraction alone is insufficient if focus and event routing remain coupled to it.

### 8.3 SDL + OpenGL for v1.0 [DECIDED] [LATER]

The **v1.0 rendering target is SDL + OpenGL**, replacing SkiaSharp as the drawing layer while retaining SDL for windowing and events. This is a future milestone, not a v0.1 dependency or an implementation claim.

Preserve the application and interaction boundaries during that transition. Evaluate text rendering, clipping, batching, resource lifetime, context creation and platform support when implementing it. Measure input-to-render latency and rendering cost before optimization; choose the OpenGL version/profile and native boundaries through a tested prototype. Keep the direct OpenGL renderer out of the v0.1 scope.

---

## 9. Language split and process boundaries

The C# / Rust / C ownership and ABI decisions are defined in sections 5a–5b. C# retains application state and orchestration; Rust begins with workspace indexing; C begins with targeted process execution. Proven third-party engines and analyzers remain candidates for inference and language integration; their bindings need not follow Oryn-owned API conventions.

Rust semantic pipelines are a future direction, not a replacement mandate for the narrow C#/Roslyn-first analysis candidate in M3. Adapter selection must follow measured capabilities and provenance requirements. Neither full semantic analysis nor a native scheduler is required for M-Native.

Use coarse-grained FFI for in-process integrations. Use separate processes and IPC where justified by fault containment or platform/tool constraints. Test cross-platform builds and packaging rather than assuming compatibility.

---

## 10. Persistence, search and RAG

### 10.1 Persistence [V0.1]

**[PROPOSED]** Use a local SQLite database per workspace or project, with migrations and transactions. Store stable entity IDs, aliases, provenance, edges, statuses, conversations, messages, cards, references, proposals, decisions and archived ideas. Keep indexes for path/symbol/text search. Prefer portable, inspectable storage and clear backup/export semantics.

Example conceptual schema (not final DDL):

```text
Project(id, root, settings)
Entity(id UUID, kind, name, code_identity?, lifecycle_state, code_presence, ...)
EntityAlias(entity_id, old_name, old_path, observed_at)
Edge(id, source_id, target_id, kind, provenance, confidence, ...)
Conversation(id, project_id, scope, entity_id?, ...)
Message(id, conversation_id, role, content, references, ...)
KnowledgeCard(id, entity_id, section, content, origin, stale, ...)
Proposal(id, source_message_id, change_set, status, ...)
Decision(id, entity_id, rationale, author, timestamp, ...)
IdeaArchive(id, proposal_id?, rationale, reconsideration_links, ...)
SourceObservation(id, entity_id, file, range, fingerprint, observed_at, ...)
```

Use migrations, foreign keys, transactional proposal acceptance and no implicit cascade deletion of valuable chat/decision history.

### 10.2 Retrieval and RAG [V0.1 basic / LATER advanced]

RAG = **retrieval-augmented generation**: find relevant information and supply it as context to the model. It need not involve embeddings or a vector database.

Start with:

1. Project file and symbol search.
2. Keyword/full-text search across knowledge cards, decisions and chats.
3. Graph-neighborhood retrieval for linked concepts, dependencies and code references.
4. Bounded context assembly with citations and source freshness.

Later consider embeddings, hybrid lexical/vector search, reranking, chunking strategies, incremental re-embedding, semantic caches and context-window budgeting. Evaluate retrieval quality on real project questions before choosing storage complexity.

**Core rule:** an agent should not repeat expensive analysis or model generation if a verified, fresh cached result already answers the request.

---

## 11. Runtime, scheduling, memory and performance

### 11.1 v0.1 scheduler

Implement a straightforward task abstraction supporting:

- priority classes: **interactive**, **analysis**, **background**;
- cancellation and bounded queues;
- dependencies and task status/progress;
- timeouts and failure isolation;
- bounded concurrency and memory pressure awareness;
- responsive graphical interface even during inference/indexing.

.NET `Task`, `CancellationToken`, channels and normal process APIs are appropriate starting points. **Do not implement a native work-stealing scheduler just to have one.**

### 11.2 Native runtime responsibilities [DECIDED]

Introduce native capabilities progressively through M-Native; advanced optimization remains future work.

#### Oryn.Runtime — Rust

Initial responsibilities are workspace traversal, file indexing, metadata collection, structured indexing results, incremental-analysis foundations and progress/cancellation where useful. Future responsibilities include semantic pipelines, cross-language symbol indexing, dependency/relationship extraction, concurrent background workloads, specialized scheduling and selected inference-support components.

Results must distinguish deterministic observations from inferred/generated information. The runtime does not own UI state, agent conversations, Git presentation or project decision history.

#### Oryn.Native — C

Initial responsibilities are native process creation, stdin/stdout/stderr management, termination, exit-status retrieval, cleanup and portable error reporting. Future work includes Unix PTY, Windows ConPTY, terminal resizing, process-tree management, platform sandbox adapters and resource controls.

Terminal emulation and process isolation are separate concerns. A PTY does not provide sandboxing.

#### Managed Execution Manager

Oryn.App owns user and agent execution requests, authorization/confirmation, session ownership, command history, output routing and lifecycle events. Native execution passes through Oryn.Interop. User and agent sessions remain logically independent: a unified Terminal view may display multiple sessions without sharing their interactive input streams. Hiding the overlay preserves the underlying session.

#### Sandbox boundary [LATER]

Enforce sandboxing through OS mechanisms. Linux, macOS and Windows backends may offer different guarantees; each must report its supported capabilities. Never advertise isolation the active backend cannot enforce. Initial process execution is not a production sandbox.

#### Scheduling and future optimization

.NET tasks remain suitable for application orchestration. A custom Rust scheduler is not required for the prototype; introduce specialized scheduling only when profiling shows a concrete benefit. Future experiments include CPU/GPU overlap, buffer lifetime, VRAM residency, KV caches, batching, model eviction and inference internals, without making them v0.1 requirements.

Key trade-offs:

- **Logical agents ≠ concurrently loaded models.** Reuse model weights and serialize/schedule workloads when hardware is constrained.
- Prioritize user interaction over background enrichment.
- Run independent CPU code analysis while GPU inference runs when beneficial.
- Batch compatible inference jobs when latency and backend support permit.
- Avoid unnecessary CPU↔GPU copies; respect backend ownership of GPU allocations.
- Use incremental indexing and dependency invalidation rather than reprocessing the entire repository.
- Consider a remote homelab provider later: local graphical frontend/tools + LAN-hosted heavy models, with local fallback and explicit data routing.

### 11.3 Instrumentation before optimization

Record at least:

- time to first token, tokens/sec, total generation time;
- model load/unload and switching latency;
- queue wait, execution duration, cancellations, failures;
- RAM/VRAM usage **where supported by the backend**;
- parser/indexing duration and invalidation counts;
- retrieval latency, cache hit rate and repeated work;
- UI input-to-render latency, frame time, rendering allocations and workload;
- UI responsiveness under background workloads and graph interaction latency;
- native workload duration and FFI overhead where measurable.

Do not claim universal GPU memory observability or portable GPU buffer control through a generic provider. Native memory observability and future optimization require platform/backend-specific investigation.

---

## 12. Security, privacy and reliability

- Local mode must not upload source code, chats or project knowledge to remote services.
- External provider use requires user-supplied API credentials and explicit consent to transmit project content.
- Prefer OS credential store or environment variables; redact keys from logs and exported diagnostics.
- Treat tool responses, repository comments and documents as untrusted input (prompt-injection boundary).
- Require appropriate confirmation for writes, shell commands and potentially destructive actions.
- Restrict file operations to allowed project roots unless user authorizes more.
- Parse/validate structured agent proposals against schemas; transactional acceptance; keep audit history.
- Handle model unavailability, malformed output, LSP crashes, window or graphics initialization failures and database failures gracefully.
- Ensure knowledge cards cite their sources; mark stale, generated or unverified material.
- Keep source observations separate from human decisions; rescans must not erase intent.

---

## 13. Suggested repository structure [PROPOSED]

This architectural target is not a claim that every path exists. The current `Oryn.slnx` contains App, Cli, Ui.Sdl and three test projects. Preserve these existing projects and the SDL3 + SkiaSharp implementation; the target uses the existing solution format rather than requiring conversion to `.sln`. Names below describe future organization, not modules to create now.

```text
oryn/
├── README.md
├── README_ORYN.md
├── Oryn.slnx
├── docs/
│   ├── adr/
│   ├── architecture/
│   │   ├── overview.md
│   │   ├── interoperability.md
│   │   ├── runtime.md
│   │   └── execution.md
│   └── roadmap.md
├── src/
│   ├── Oryn.Cli/                 # Existing launch/composition project
│   ├── Oryn.Ui.Sdl/              # Existing C# graphics adapter, preserved
│   ├── Oryn.App/
│   │   ├── Application/
│   │   │   ├── Bootstrap/
│   │   │   ├── Configuration/
│   │   │   └── Lifecycle/
│   │   ├── UI/                   # Managed presentation/interaction ownership
│   │   │   ├── Rendering/
│   │   │   ├── Layout/
│   │   │   ├── Docking/
│   │   │   ├── Input/
│   │   │   ├── Components/
│   │   │   └── Panels/
│   │   │       ├── Workspace/
│   │   │       ├── Chat/
│   │   │       ├── Code/
│   │   │       ├── Visualizer/
│   │   │       ├── Terminal/
│   │   │       └── Git/
│   │   ├── Workspace/
│   │   ├── Agents/
│   │   ├── Git/
│   │   ├── Execution/
│   │   ├── Events/
│   │   └── Services/
│   └── Oryn.Interop/
│       ├── Loading/
│       │   ├── NativeModuleLoader.cs
│       │   └── NativeModuleLifetime.cs
│       ├── Contracts/
│       ├── Runtime/
│       │   ├── RuntimeApi.cs
│       │   └── RuntimeBindings.cs
│       ├── Native/
│       │   ├── ProcessApi.cs
│       │   └── ProcessBindings.cs
│       ├── Handles/
│       └── Marshalling/
├── native/
│   ├── include/
│   │   ├── oryn_abi.h
│   │   ├── oryn_runtime.h
│   │   └── oryn_process.h
│   ├── runtime/
│   │   ├── Cargo.toml
│   │   └── src/
│   │       ├── lib.rs
│   │       ├── ffi.rs
│   │       ├── indexer/
│   │       ├── analysis/
│   │       └── scheduler/
│   └── process/
│       ├── CMakeLists.txt
│       ├── include/
│       └── src/
│           ├── process.c
│           ├── platform.h
│           ├── linux/
│           ├── macos/
│           └── windows/
├── tests/
│   ├── Oryn.App.Tests/
│   ├── Oryn.Smoke/               # Existing CLI/domain smoke
│   ├── Oryn.Ui.Smoke/            # Existing SDL smoke
│   ├── Oryn.Interop.Tests/
│   ├── native-runtime/
│   ├── native-process/
│   └── integration/
├── samples/
│   └── workspaces/
├── build/
│   ├── scripts/
│   └── packaging/
└── .github/
    └── workflows/
```

### 13.1 Repository rules

- Inspect the current solution before moving files; reorganize incrementally without broad rewrites to match the tree.
- Preserve SDL3 + SkiaSharp and keep application state and presentation in C#.
- Do not create empty placeholder subsystems or additional assemblies solely to reflect concepts.
- Keep native ABI contracts centralized and avoid circular dependencies.
- Keep native implementation details out of application panels.
- Do not commit generated artifacts or platform binaries unless explicitly required.

### 13.2 Build system

The future build must coordinate .NET, Cargo and CMake compilation, native discovery, platform packaging, ABI tests and cross-language integration tests. Packaging must account for Windows/Linux/macOS, supported CPU architectures, native dependencies and runtime identifiers. A single documented developer command should eventually build the managed app and required native modules. Current working .NET build/run commands remain in README.md; no native build command is claimed yet.

### 13.3 Future modularization

Graph, Knowledge, Agents, Inference and Storage may become separate .NET projects when coupling or tests justify them. Prioritize ownership, testability and stable contracts over project count. The proposed folders do not require an immediate project or graphics reorganization.

---

## 14. Practical roadmap for Codex

### M-Native — Multi-language interoperability foundation [V0.1]

**Objective:** working C# / Rust / C interoperability without destabilizing the SDL3 + SkiaSharp prototype. This milestone may run alongside early application development; M0–M7 retain their numbers and objectives. Nothing in this section is implemented by the documentation update.

#### Phase A — Repository preparation

Inspect the solution; introduce Oryn.Interop, Rust/C module directories, public ABI headers and initial native build configuration while preserving application behavior.

#### Phase B — Native loading

Implement dynamic loading, versioned API-table resolution, typed `delegate* unmanaged[Cdecl]` bindings, compatibility checks, deterministic cleanup and missing/incompatible-module diagnostics.

#### Phase C — Rust integration

Implement a minimal workspace indexer accepting a workspace path and returning structured file metadata through Rust's C ABI and Oryn.Interop. Do not build a complete semantic analyzer or custom scheduler.

#### Phase D — C integration

Implement a minimal backend that launches a process, captures output, reports exit status and releases resources safely. Do not build a complete terminal emulator or production sandbox.

#### Phase E — Application integration

Connect indexing to a managed workspace service and process execution to a managed execution service. Surface results through existing components, keep native work off the UI thread and verify error handling and shutdown.

#### Acceptance criteria

1. Oryn launches with its existing SDL3 + SkiaSharp interface.
2. C# loads the Rust and C modules successfully.
3. ABI compatibility is validated.
4. Rust indexes a real workspace.
5. C executes a real process.
6. Managed services consume structured results.
7. Native resources are released correctly.
8. Native errors produce meaningful diagnostics.
9. Automated integration tests pass on the supported development platform.
10. Incomplete platform support is documented.

#### Non-goals

Custom native rendering, inference kernels, advanced scheduling, full semantic graph analysis, production sandboxing and complete terminal emulation.

### M0 — Existing graphical workspace and stabilization

**Preserve and stabilize:** existing CLI/configuration, SDL3 window/events, SkiaSharp drawing, project directory browsing, left file tree, central Chat/Sources/Visualiser tabs and demo visualizer with optional docking. Keep state outside graphics libraries; complete remaining interaction, scaling and platform validation before claiming M0 complete. The graphical prototype already exists; do not bootstrap a replacement TUI.

**Done when:** launches on the primary development OS; keyboard navigation works; resizing doesn't crash; a project folder can be browsed; window startup/shutdown release native resources cleanly.

### M1 — Chat + inference provider

**Build:** local inference adapter (choose Ollama or llama.cpp after a brief proof of concept), streaming chat, cancellation, errors, persisted global project chat, model configuration and core-only operation.

**Done when:** ask a question about an opened project; see streamed response; cancel safely; restart and recover conversation. No API key required.

#### M1 completion plan — persistence, context, setup and benchmarks [PROPOSED]

The following work items refine M1 and its multi-model follow-up; they are not implemented merely by being listed here. See [the M1 data/context/setup plan and user-run validation protocol](docs/m1-completion-and-validation.md).

**Implementation snapshot (2026-10-16):** lifecycle ownership/cancellation, ID-based journal/gzip storage and legacy migration, bounded model context, first-run hardware suggestions, consented pinned installation and manual profile selection are now implemented in the working tree. The solution and prepared tests compile; functional tests, real setup/model probes and benchmarks remain to be executed by the user. See [the implemented M1 guide](docs/m1-llama-cpp-integration.md). Retrieval indexing, semantic summaries and HIE remain future work.

1. **Backend lifecycle:** reliable startup, cancellation, deadlines, bounded concurrency, port ownership and shutdown without orphaned processes.
2. **Project history:** investigate stable project UUIDs and a configurable local data repository, an active recoverable journal and immutable sealed segments compressed without loss. Load recent history lazily; retain originals as canonical sources and derive a project-scoped retrieval index later. Compression is not semantic summarization and does not reduce the model's token budget. Preserve chats and human decisions across migration, interruption, corruption and export/import.
3. **Context budgets:** count input with the selected model's tokenizer where available, reserve output/template/system space, and select bounded recent turns plus explicit source excerpts. Archive older turns without deleting them; later retrieval/summaries must retain references and distinguish generated content from original messages.
4. **First-run setup and model profiles:** inspect available RAM, supported backend/devices and VRAM, explain candidate profile/download sizes, then suggest rather than force an installation. Validate a small candidate with a user-initiated local probe before promoting it. Support smaller CPU-oriented profiles, explicit overrides, offline/no-model operation and single-core-model operation; raw hardware capacity alone does not predict useful latency.
5. **User-run test battery:** prepare versioned programming, structured-text, logic and ambiguity tasks with objective expected outputs, plus lifecycle/persistence scenarios. The user executes tests; record missing, failed, partial and passed cases separately. Closed-answer checks do not establish general code-generation or architecture ability.
6. **Integration acceptance:** update obsolete demo expectations and verify the complete chat/configuration/restart/cancellation path, including explicit distinctions between no-model mode and core-only (one primary model).
7. **End-to-end benchmarking:** measure the actual Oryn path as well as standalone inference, distinguish cold/warm and Fast/Core runs, include failures/loading/switching/repair costs, and report correctness, time to first visible output, total latency, tokens and observed resources. Treat unavailable instrumentation as unavailable, never as a zero or a measured peak. Keep this baseline separate from future HIE A/B/C experiments.

#### Follow-up — Multi-model integration [PROPOSED]

**Next inference priority:** preserve the working local chat while introducing model profiles and explicit user-selected **Fast / Core** modes. This is planned infrastructure, not a claim that automatic multi-model orchestration already works.

- Configure model location, backend/template compatibility, sampling, reasoning limits and context budgets per profile. GPT-OSS and Qwen3-8B are current measured candidates, not universal requirements.
- Support selection, lazy loading, reuse, unloading and visible switching states. Start with one large model resident at a time, a bounded inference queue and reliable cancellation/shutdown; preserve core-only operation.
- Track loading/switching time, input/output token usage, first visible response, generation rate and end-to-end duration per model. Report observed RAM/VRAM when available; distinguish measurements from estimates and account for weights, KV cache and context budget.
- Keep logical agent roles independent from physical model selection. A learned tiny-model router is optional and must demonstrate value over manual selection or deterministic rules, including its loading and inference overhead.

**Verification target:** select Fast or Core, observe the selected model and loading state, generate and cancel a response, switch without leaving an orphaned backend or assuming simultaneous residency, and compare recorded metrics. Instrument these paths before automating model choice; a sophisticated scheduler is not a prerequisite for useful tools.

#### Research follow-up — Oryn Hybrid Intelligence Engine (HIE) [PROPOSED] [LATER]

**Status and placement:** architectural research to explore after the multi-model integration above. HIE does not replace that foundation, authorize a rewrite, introduce a new required milestone, or gate M2–M7 and the v0.1 release. This section records a hypothesis and evaluation plan, not a proven performance improvement or an instruction to implement all proposed engines.

##### Vision and hypothesis

Explore a modular hybrid intelligence runtime combining general-purpose LLMs, Fast/Core model profiles, deterministic tools, retrieval, logical specialized agents and, only when justified, narrow learned subsystems or domain adapters. The objective is **higher validated task quality under measured resource and latency constraints**, rather than larger models, more components or guaranteed lower latency on every request.

The candidate flow is:

```text
Request + project context
          |
          v
Candidate problem representation (when useful)
          |
          v
Routing + planning, constrained by independent policy
          |
          +---- Deterministic tools / retrieval
          +---- Fast / Core LLM and logical specialized agents
          +---- Optional narrow learned subsystem
          |
          v
Execution + validation
          |
          +---- Result with evidence
          +---- Clarification / bounded correction / replanning
```

Paths may be combined. Probabilistic inference remains useful for language interpretation, synthesis and proposing solutions; formalizable operations should use tools that can supply verifiable evidence. Decomposition alone does not establish the capabilities of a larger model.

##### Candidate Problem Intermediate Representation (Problem IR)

Investigate a revisable representation of user intent, inputs/outputs, relevant entities/symbols, constraints, assumptions, missing information, ambiguities, candidate operations, required capabilities and validation criteria. Start with narrow executable tasks rather than a universal problem ontology. Exploratory conversations need not pass through a detailed IR.

For “Create a function that returns elements sharing the same ID in a list”, a candidate interpretation might be:

```json
{
  "intent": "generate_function",
  "operation": "duplicate_detection",
  "key": "ID",
  "input": "collection<T>",
  "output": null,
  "language": null,
  "ambiguities": [
    "duplicate_groups_or_flat_results",
    "all_matching_elements_or_only_extra_occurrences"
  ],
  "constraints": { "preserve_order": null },
  "validation_criteria": []
}
```

This is a hypothesis about the request. Schema validity does not establish semantic correctness. Keep explicit user requirements separate from generated assumptions, attach source references where applicable, and retain unresolved alternatives. Contradictory evidence or failed validation may require clarification or replanning. A deterministic engine can execute the wrong interpretation correctly; do not silently promote an inferred interpretation to a user-approved decision.

Problem IR and execution traces must reference the **shared project knowledge model** and stable UUIDs where available. They must not become a competing opaque memory or be confused with the language-neutral code IR in section 7.1.

##### Candidate engines and responsibility boundaries

- **Deterministic engines:** parser/compiler/static-analysis integration, symbol/type resolution, explicit constraint checks, algorithm catalogs, narrowly scoped templates and test execution. Prefer these when the task is sufficiently formalized; report unsupported or partial results.
- **Specialized neural engines:** optional classification, candidate ranking, error categorization, semantic similarity or retrieval reranking. Confidence/risk estimates require calibration. Compare CPU and GPU execution including batching, transfer, residency and contention; GPU use does not imply lower latency.
- **Specialized LLM agents:** initially configure roles such as Coder, Analyzer and Architect using existing models, instructions, contexts, tools, output contracts and evaluation suites. Several roles may share one model. Dedicated models or LoRA adapters remain optional experiments.

Keep the following responsibilities logically distinct without requiring four services or new assemblies:

| Responsibility | Purpose |
|---|---|
| Semantic router | Identify required capabilities and candidate execution paths; permit abstention or clarification. |
| Policy engine | Enforce permissions, tool access, budgets and approval boundaries independently of model instructions. |
| Execution planner | Build bounded plans, manage dependencies and handle fallbacks or replanning. |
| Resource scheduler | Manage queueing, concurrency and model residency within measured memory/context budgets. |

The scheduler must account for model weights, KV cache, context size, optional subsystem memory, transfer costs, GPU contention and switching overhead. Begin with simple bounded tasking; this proposal does not authorize custom inference kernels, a GPU allocator or a complex native scheduler.

##### Experimental baselines and observability

Compare three configurations using a fixed, versioned suite of straightforward, ambiguous and multi-domain programming tasks:

| Configuration | Execution | Evaluation purpose |
|---|---|---|
| A — Standalone LLM | Request → GPT-OSS/reference model → response | Establish raw model quality, token use, latency and resource baseline. |
| B — LLM with tools | Reference model + sourced retrieval/index + compiler/static analysis/tests → result with checks | Measure the benefit of context and deterministic tooling without a dedicated HIE planner. |
| C — HIE prototype | Context + candidate Problem IR + router/planner/policy + selected engines + validation/replanning | Test whether explicit representation and routing improve on B. |

For **B versus C**, match the available models, tools, context, hardware conditions, acceptance criteria and execution budgets as far as possible. Record differences rather than crediting all gains to orchestration. A and B intentionally differ in tool access; their scores are not interchangeable with standalone model benchmarks.

Measure functional correctness, validation outcomes, total latency, time to first visible content, input/output tokens, number of LLM/tool calls, loading/switching overhead, observed memory peaks, CPU/GPU utilization, recovery rate and resource/time cost per successful task. Include failed attempts, repairs and clarification overhead. Distinguish reasoning tokens from visible output where supported, cold from warm runs, sampled maxima from true peaks, and raw inference from end-to-end Oryn performance. Report latency distributions and the limits of small evaluation suites.

Routing can have several valid answers: evaluate task success and execution cost rather than assuming one model label is always correct. Keep evaluation tasks separate from examples used to tune routing, prompts or adapters. Compilation, test success and satisfaction of the user's actual intent are distinct checks; generated tests and repeated model agreement are not independent proof.

##### Progressive research sequence

1. **Preserve the operational LLM foundation.** Keep backend, streaming, cancellation, persistence and core-only behavior functional.
2. **Complete the multi-model slice above with instrumentation.** Start with explicit selection; do not require automatic routing to proceed.
3. **Introduce execution contracts and observability.** Record request/context references, authorized operations, budgets, outcomes and evidence. Start with a few typed contracts; reuse existing application tasking and knowledge storage.
4. **Establish configuration B.** Add sourced retrieval and deterministic verification through the relevant language-analysis, tooling and retrieval milestones. Do not postpone useful tools until a complete multi-model scheduler exists.
5. **Prototype a small configuration C.** Test revisable IR and capability routing on a few recurring categories; bound retries and preserve clarification and single-model fallbacks. Promote the approach only if it improves on B under stated budgets.
6. **Consider learned specialization after collecting verified outcomes.** Specify a recurring task → configure a logical agent/tools/indexes → collect traces → independently verify/filter examples → optionally train/distill an adapter or narrow model → benchmark on held-out tasks → promote or reject a versioned update. Capable models may assist dataset/specification creation, but their outputs are not automatically ground truth. Updates must be reversible; never silently train production weights from unverified interactions.

**Research acceptance:** retain additional components only when reproducible results demonstrate a meaningful benefit over simpler baselines. Longer explanations, more calls or a more elaborate architecture are not evidence of better engineering assistance. Narrow learned engines, automatic routing and fine-tuning remain research options, not new v0.1 obligations.

### M2 — Persistence + foundational graph

**Build:** SQLite migrations; UUID entities/edges; project/concept nodes; statuses; manually create/edit/link nodes; graph list/viewport; simple navigation and progress summaries.

**Done when:** create a concept, link it to a project component, mark status, restart and see all data unchanged.

### M3 — Language analysis MVP

**Build:** first language adapter (C#/Roslyn recommended), project/file/type/method discovery, source pointers and selected relationships, evidence metadata, incremental refresh where feasible, simple reconciliation and manual re-link on ambiguity.

**Done when:** open a small sample repository; graph is populated from real source; a rename or move doesn't silently steal a chat; unsupported relationships are visibly absent/uncertain.

### M4 — Contextual chats + knowledge cards

**Build:** attach conversation to UUID; Show Chat action; card tabs (overview, technical, references, project role, decisions); references/provenance; persisted content; manual edits.

**Done when:** discuss a concept, close the chat, reopen from the node, and retrieve its rationale and documentation after restart.

### M5 — Multi-agent proposal pipeline

**Build:** coordinator + assistant + graph-curation role, typed proposal schema, model-backed generation, validation, review/accept/reject/archive UI, provenance links to originating messages; Idea Box.

**Done when:** discuss a proposed subsystem in global chat; agent offers a new node/edge; user can accept it, edit it or archive it without silently changing approved architecture.

### M6 — Retrieval, detail views and limited graph depth

**Build:** full-text and graph-neighborhood retrieval, context citations, scope/depth controls, method-level graph where analyzer supports it; optional beta call graph/CFG **only when analyzer-backed**.

**Done when:** user can ask why a node exists, get the relevant project decision and source references, and navigate at least two meaningful graph abstraction levels.

### M7 — Stabilization and v0.1 release

**Build:** cross-platform smoke tests, graphics/platform failure handling, model/backend failure handling, backup/export, logging, security review, performance baseline and user documentation.

**Done when:** documented install/run works on Windows, Linux and macOS (with platform limitations documented), no data loss on restart, graphical interface remains responsive under background tasks, and key workflows have automated tests.

**Roadmap note:** milestones are a dependency-oriented guide, not permission to defer core v0.1 features indefinitely. Implement vertical slices and ship a usable result at each stage. If a proposed library proves unsuitable, write a brief ADR and substitute a compatible adapter rather than rewriting the product design.

---

## 15. Test strategy and acceptance criteria

**Automated:**

- Graph UUID stability across rescans; no accidental identity reassignment.
- Proposal validation rejects malformed, unauthorized or contradictory updates.
- Lifecycle transitions preserve decision history and do not imply code correctness.
- Archived ideas remain searchable and can be restored as proposals.
- Storage migrations and restart recovery; no chat loss.
- Retrieval returns traceable sources and marks stale data.
- Model cancellation and backend failure do not block UI.
- Input/event routing remains testable independently of SDL/Skia rendering.
- Native ABI version, structure layout/size and capability validation.
- Native handle cleanup, operation shutdown and missing/incompatible-library behavior.
- Rust indexing and C process-execution integration through managed services.
- Cross-platform loading and packaging where supported; document untested platforms.

Native interoperability checks are planned acceptance coverage, not tests already present or executed.

**Manual scenarios:**

1. Open an unfamiliar project, browse files and discuss architecture.
2. Select a code element and inspect what it does, how, why and where it is documented.
3. Propose a component in global chat, review the graph diff, accept it as **À valider**, then promote to **Validé**.
4. Mark it **En cours**; later confirm **Implémenté** with relevant code/tests.
5. Archive an alternative in the Idea Box and retrieve its rationale later.
6. Rename a code symbol and ensure attached chat is preserved or flagged for manual reconciliation.
7. Disable inference/network and still browse the project and stored knowledge.
8. Run with a small window and with a backend unavailable; recover gracefully.

---

## 16. Decisions, proposals and open questions

### Decisions to preserve

- Oryn is **local-first, keyboard-first and terminal-inspired**, with **SDL3 + SkiaSharp in v0.1**, then **SDL + OpenGL for v1.0**. A custom TUI renderer is not required.
- C# application ownership, Rust runtime workloads and C OS integrations form the decided multi-language architecture; M-Native validates useful interoperability during the prototype.
- Oryn-owned modules expose independent versioned C ABI tables through explicit managed function-pointer bindings.
- Graphics remain in C#; native ABI separation is not process isolation.
- Dedicated Chat/Code/Git/Visualizer views and a persistent-session Terminal overlay are the target presentation; Git and Terminal are planned.
- A rich visualizer is a core feature, not an optional later dashboard.
- The visualizer represents **implemented code and conceptual architecture**.
- Main lifecycle is **À valider / Validé / En cours / Implémenté**, with a separate Idea Box.
- Chat is both **global** and **persistent per element**.
- Knowledge cards teach technologies and explain project-specific rationale.
- Stable UUIDs plus conservative reconciliation protect element history.
- Deterministic analyzers, not LLMs, provide authoritative code/CFG observations.
- Multi-agent roles are central; agents can share physical models.
- The SDL + OpenGL renderer targets **v1.0**; the low-level runtime remains future work. Neither is a v0.1 blocker.
- Oryn should eventually be able to help build Oryn itself.

### Choices to verify experimentally

- SDL/.NET bindings, SkiaSharp drawing backend, text input, display scaling and native packaging; the SDL + SkiaSharp frontend choice is decided.
- Local inference: llama.cpp vs Ollama, packaging and streaming ergonomics.
- Model lineup: Qwen3 0.6B / 4B / 8B candidates vs a single quantized core model; benchmark real tasks and memory.
- HIE research after multi-model integration: compare a reference LLM, the same LLM with tools, and explicit problem representation/routing before adopting specialized learned engines or adapters; see the research follow-up under M1.
- C# Roslyn-first integration versus a narrower multi-language syntax-only first pass.
- SQLite schema, full-text indexing and when embeddings add enough value.
- Native API details, cancellation, table-capacity negotiation, build/discovery and packaging; the language ownership and function-pointer direction are decided.

### Open questions (do not block first coding session)

- Exact graphical UI keymap, mouse interaction policy and panel docking UX.
- Exact schema for graph proposal diffs and conflict resolution.
- Granularity of knowledge-card generation and refresh.
- Packaging/install UX across three OSes.
- OpenGL version/profile and platform support for the v1.0 renderer.
- Homelab inference protocol, model residency policy and remote privacy controls.
- Long-term OpenGL renderer, scheduler and inference engine internals.

---

## 17. Future roadmap — vision, not v0.1 obligations

### v1.0 and beyond

- **[DECIDED] v1.0: SDL + OpenGL**, replacing the SkiaSharp drawing layer while preserving application state, commands and SDL event handling.
- Richer language integrations and a Mason/LazyVim-inspired tool catalog/package manager.
- More accurate and synchronized Call Graph + CFG, deeper semantic zoom and data/event flow views.
- Stronger identity reconciliation across renames, moves, refactors, splits and merges.
- Incremental hybrid RAG, reranking and specialized knowledge agents.
- Sophisticated scheduler, task DAGs, batching, CPU/GPU overlap and memory-aware model residency.
- Advanced Rust/C experiments in GPU buffers, KV caches and inference runtime internals; initial indexing/process modules belong to M-Native in v0.1.
- Remote model execution on a personal homelab, optional external API providers, local fallback.
- Performance visualization of Oryn's own tasks, inference and architecture **inside Oryn**.
- Extension ecosystem for languages, tools, visualization and inference providers.

### The ultimate loop

1. Build a functional v0.1 with pragmatic dependencies.
2. Open the Oryn repository in Oryn.
3. Use its own project graph, contextual chats and knowledge cards to plan improvements.
4. Replace selected dependencies with carefully measured homemade implementations.
5. Repeat without sacrificing usability, provenance or the user's control.

---

## 18. Immediate instruction for the next development session

**Preserve the running graphical prototype and advance through the requested milestone.**

Inspect the existing prototype, machine, toolchains and latest handoff before the next implementation task. Continue the requested **M0** stabilization or **M-Native** slice, preserving the working SDL3 + SkiaSharp frontend documented in [ADR 0006](docs/adr/0006-sdl3-skia-raster-m0.md). Keep independent application state, left FileExplorer, central tabs, optional docking, focus and clean shutdown. Maintain usable build/run instructions and meaningful smoke coverage. Documentation-only requests do not authorize native implementation, UI rewrites, dependencies or placeholder modules. Keep the path to M1 without attempting the entire roadmap.

> **Oryn is not meant to write software instead of its author. It is meant to make exploring, understanding and building software enjoyable again.**
