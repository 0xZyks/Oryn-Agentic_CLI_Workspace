# ADR 0017 — Native Markdown rendering with source-based selection

- Status: implemented in the working tree; compiled and visually reviewed with render-only fixtures. User-run native interaction/tests pending.
- Date: 2026-10-16.

## Context

The user requested Markdown in the native chat after implementing full-width messages, separators, copy icons and mouse selection. Formatting must preserve streaming, stable message identities and the original persisted text.

## Decision

Use pinned **Markdig 1.3.2** (BSD-2-Clause), already available in the local NuGet cache, for CommonMark parsing and selected extensions. Convert its precise source spans to small toolkit-independent blocks/runs in App; paint those runs with Skia in the existing graphical frontend. Keep font resources and measured layout in the renderer and keep selection anchors in App. No HTML/webview renderer or new assembly is introduced.

Support headings, emphasis, strong/combined emphasis, strikethrough, inline/fenced/indented code, nested ordered/bullet/task lists, quotes, horizontal rules, links and pipe tables. Code remains literal, with a language header and background; partial fences remain renderable during streaming. Equal-width table columns wrap cells; very narrow columns fall back to stacked cells. Link labels are styled, without automatic URL opening. Images show alt text; no remote resource loader is present. HTML parsing is disabled and HTML-looking text remains literal.

Keep persisted/inference messages unchanged. The message copy icon returns original Markdown. Mouse/Ctrl+C selection uses the visible-text projection, excluding hidden markup and link targets; selection still anchors to source positions so reflow and model streaming do not reassign it to a different message. Transformed atomic tokens such as entities map to their whole source span. Tables copy cells separated by tabs. The existing selection state remains independent of SDL/Skia.

Cache layout by message text and width. Bound parsing input to 262144 characters, with literal-text fallback for larger messages, and bound conversion traversal depth. Do not execute code, HTML or model-generated instructions during rendering.

## Alternatives

A homemade regular-expression parser would require maintaining Markdown ambiguity, nesting, escapes and streaming edge cases. A browser renderer would add a second UI/runtime and complicate source-based selection, offline behavior and native packaging. A syntax-highlighting engine is unnecessary for this formatting slice; code highlighting and richer media can be evaluated separately.

## Sources and verification

The [pinned Markdig documentation](https://github.com/xoofx/markdig/tree/1.3.2) documents its AST, precise source locations, CommonMark syntax and extensions. Package metadata pins version/license/hash; updated lockfiles retain the existing Linux target.

The complete solution and prepared App/UI scenarios compile. Render-only PNG fixtures cover full Markdown, wide/compact/high-density layouts, element discussions, selected text and an unfinished code fence. These were visually inspected. Tests and native SDL mouse/clipboard checks remain for the user; no model inference or benchmark was run.
