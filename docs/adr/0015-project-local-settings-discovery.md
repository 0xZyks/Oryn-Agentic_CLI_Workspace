# ADR 0015 — discover workspace settings from the project root

- Status: accepted.
- Date: 2026-10-16.

## Context

Requiring `--config` for model and workspace settings made setup depend on a machine-specific config file path. Oryn already receives a project root and supports an ignored project data directory for local chat state. Model/runtime paths also support resolution relative to that root.

## Decision

When no `--config` argument is provided, load `oryn.settings.json` from the opened project root if it exists; otherwise use defaults. An explicit `--config` path takes precedence. Relative model/runner paths continue to resolve against the opened project root, not the process working directory or the config file's location.

The settings file is at the project root so teams can choose to version a portable configuration. The GGUF and backend binaries may stay in ignored local storage such as `.local/`. No secrets belong in this file.

## Consequences

Opening another project loads its own settings automatically. `--check` reports the selected settings source. Project-relative paths can be shared when developers use the same folder layout, while platform-specific inference runtime support remains a separate concern; the current runner is Linux/CUDA-specific.

## Verification

The App project and SDL project must compile, and documentation must distinguish path portability from platform runtime support. A later integration smoke should exercise explicit override, project auto-discovery, defaults when no project file exists, and a relative model path.
