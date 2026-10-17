# Scripted M1 battery through Oryn

The 12 public JSON cases can now be executed without manually pasting each prompt. This is a developer runner in the existing UI smoke executable, **not a new Tools menu**. It uses the actual application chat controller, context preparation, owned llama.cpp backend, journal and SDL/Skia presentation. It does not invoke the normal stub-provider smoke suite or execute generated code.

From the repository root, choose a new directory for every campaign:

```sh
dotnet build Oryn.slnx --no-restore
python3 scripts/benchmarks/m1.py prepare --config oryn.settings.json --output artifacts/m1/automated-next
dotnet run --project tests/Oryn.Ui.Smoke --no-build --no-restore -- --m1-benchmark artifacts/m1/automated-next
python3 scripts/benchmarks/m1.py grade --run artifacts/m1/automated-next
```

The selected model must already be installed. Close any other inference session using the configured port or GPU before running. A desktop session and GPU access are required for this real-window mode. The runner refuses existing answers or initialized scratch histories. Each case gets a fresh workspace, controller, owned server process and SDL window. The normal shutdown path releases the backend before the next case. Expected answers are not supplied to the model: the runner uses only exported prompts and grading runs afterward.

For each case it records the raw answer, final application status, a screenshot, elapsed submission-to-presentation and completion times, and token counts exposed in the application status. It then closes the controller and reopens the store to compare persisted message IDs, content and status. `execution.json`, `measurements.csv`, `run.json` and the grader's `report.json` retain the results. Remaining cases stay unrun if the harness or backend fails; rerun in a new directory instead of overwriting the attempt.

The first-text timestamp is sampled in the **callback after SDL_RenderPresent**, at a configured 60 Hz. It does not measure physical display/compositor latency. Requests enter through the controller text/keyboard API, so this mode does not validate native SDL keyboard input or clipboard events. Model startup, context preparation and persistence are included in each request's elapsed time. Restarting the server does not establish a cold disk cache. Load-only duration, separate reasoning-token counts and per-case RAM/VRAM stay blank when not independently observed. This single pass supplies observations, not a latency distribution across repeated identical tasks.

## Recorded campaign, 2026-10-17

Local ignored artifacts: `artifacts/m1/automated-gptoss-20261017-02/`. GPT-OSS-20B MXFP4, SHA-256 verified against the pinned catalog; core profile, context 4096, 1024 output budget, two CPU expert layers, Flash Attention on, reasoning effort low/budget 256. Actual profile and source hashes are retained in `run.json`.

- 12/12 generations completed and all 12 histories matched after reopening.
- Strict public JSON quality checks: **11/12 passed**. C04 was wrong: the model returned `A=true,B=true` where the default-interface-method example requires `A=false,B=true`.
- First presented answer: median **4.92 s**, range **3.77–19.15 s**. Completed frame: median **5.30 s**, range **4.10–19.23 s**. Startup included; OS caches uncontrolled. Output-token counts include whatever usage the backend reports; no separate reasoning/visible split is claimed.
- The first attempt (`automated-gptoss-20261017-01`) remains intact: C02/C03 had transport failures, C04 was wrong, and C05 stopped on a Markdown rendering exception. It is a partial failed campaign, not another complete quality run. The transport failures did not recur in campaign 02; their cause has not been established.

The campaign revealed that Markdig can expose a fenced block with zero lines and a null underlying line array while only the opening fence has streamed. Parsing now iterates by the line count, avoiding LINQ on that null array. Regression cases cover empty/open JSON fences. Backend process failures during generation now include the bounded, masked owned-process diagnostic tail when available. Successful completion of campaign 02 does not prove the earlier transport errors are permanently resolved.

This battery is a small public development suite, not full M1 acceptance, a general coding benchmark, or Windows/macOS certification. Cancellation, failure injection and native interaction scenarios remain separate checks.
