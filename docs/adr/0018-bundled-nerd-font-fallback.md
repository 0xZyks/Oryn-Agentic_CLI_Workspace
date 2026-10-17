# ADR 0018 — Offline Nerd Font symbols and Unicode fallback

- Date: 2026-10-17.
- Status: implemented; build and render-only verification recorded in the session log. User-run suites and native SDL interactions remain pending.

## Context

The user reports unreadable missing-glyph boxes in model replies and requests a Nerd Font before continuing M1 validation. The painter selects one installed monospace family. Skia's DrawText does not automatically substitute another typeface for absent glyphs.

## Decision

Embed the unmodified Symbols Nerd Font Mono v3.4.0 (approximately 2.5 MB) and its MIT license in Oryn.Ui.Sdl. Retain the existing monospace text face. For missing glyphs, try the bundled symbols, then Skia's local system character matching. On Skia builds without a working character matcher, query installed Unicode/emoji families explicitly. No system font installation, remote font request or model change is required. The upstream [Symbols Only documentation](https://github.com/ryanoasis/nerd-fonts/blob/v3.4.0/patched-fonts/NerdFontsSymbolsOnly/README.md) describes this fallback use. Provenance, SHA-256 and license are recorded beside the asset.

Use the same resolved fonts and advances for drawing, wrapping, Markdown prefixes, clipping and selection hit testing. Preserve Markdown size/bold/italic. Resolve whole graphemes where supported, then Unicode scalars, including supplementary private-use Nerd icons. Keep source messages, prompts, persisted data and copied text unchanged. Unsupported format controls and variation selectors do not draw missing-glyph boxes; this does not implement complex shaping or composed emoji sequences.

Font matching remains in the SDL/Skia adapter; App and providers have no font dependency. Cache up to 2048 character matches and 64 system faces per painter; dispose native fallback fonts before their typefaces. The embedded font travels with the renderer assembly through project references, publish and tool packaging.

## Alternatives and limits

Selecting an installed Nerd Font alone would depend on each machine's setup. Replacing the whole text face with a patched font adds a larger asset and changes existing typography. Symbols-only preserves the current text appearance and works without a network or installation.

Nerd Fonts supplies icons, not every Unicode character. System fonts can extend coverage for CJK, emoji and mathematical symbols. Characters absent from all available faces may still display as missing glyphs. Complex-script shaping, bidirectional layout and emoji ligatures remain outside this slice; no universal Unicode support or new platform certification is claimed.

## Verification

Compile the solution and prepared checks. `dotnet run --project tests/Oryn.Ui.Smoke --no-build --no-restore -- --font-preview artifacts/m1/font-ui-preview` renders local specimens only (no SDL window, suite, model or project history), including Nerd icons in BMP/supplementary planes, Markdown styles/code, Unicode, selection, compact layout and high density. Its glyph-coverage report records actual faces selected on the current machine.

FontFallbackChecks is included in the user-run UI smoke suite and checks actual glyph availability, supplementary characters, style preservation and measurement consistency. Native selection/clipboard behavior remains to be verified by the user with the rest of M1.
