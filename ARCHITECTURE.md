# ECAssistant Console — Architecture

**Updated:** 2026-09-22 (release audit: Tests deps Core 12.9.11 + TestSupport 12.9.11; v1.0.8 shipped against TUI 12.9.11 which itself pinned Core 12.9.10 — fix train pending: TUI 12.9.12 + Console 1.0.9. Previous: v1.0.7 — Core 12.9.9 process-backend fix + always-alive server; LLM server 14.9.5; TUI 12.9.10)
**Previous:** 2026-09-18 (v1.0.2 — true thin host; all deps via GitHub Packages; setup logic in Core)
**Build:** 0 errors, 0 warnings

## Overview

ECAssistantConsole is a minimal .NET 8 console executable — a **dumb launcher** for ECAssistant. It contains zero application logic: setup orchestration lives in Core (`FirstRunOrchestrator`), presentation lives in TUI (`AppController`). Console only dispatches, validates startup preconditions, and wires the composition root.

## Project Structure

```
ECAssistantConsole/
├── ECAssistantConsole.csproj      ← Console exe, PackAsTool (thin dotnet tool ~2.8 MB)
│     PackageId=ECAssistant.Console; deps: ECAssistant.Core/TUI via GitHub Packages
│     NO ECAssistant.LLM.Server reference — the server is NOT embedded
├── Program.cs                     ← Entry point (--test → ConsoleTestHost, else ConsoleApplication)
├── ConsoleApplication.cs          ← Thin host: Core FirstRunOrchestrator → composition root → TUI AppController
├── ConsoleTestHost.cs             ← --test harness host
├── HostPaths.cs                   ← User config dir resolution
└── Tests/                         ← ECAssistantConsole.Tests (xunit; excluded from app compile)
```

## Setup Flow (v1.0.2 — orchestrator moved to Core)

Console calls `FirstRunOrchestrator` (ECAssistantCore/Setup) — the SAME orchestrator the TUI uses:

1. Disk-truth detection (`FirstRunDetector`): gguf files, llm-server.json entries, server binary
2. Wizard runs only when needed; the LLM server binary is installed **inside the wizard**,
   exactly when local chat OR local embeddings is chosen (via `ServerInstallCoordinator` +
   `NuGetServerFetcher` downloading `ecassistant.llm.server` from nuget.org). Pure remote
   users get zero LLM footprint. See Core ARCHITECTURE.md for the full state matrix.
3. Host then validates: remote configured OR local model usable → composition root → AppController

## Dependency Flow

```
Program.cs → ConsoleApplication.RunAsync()
  ├── new FirstRunOrchestrator(userConfigDir, ConsoleSetupUi).RunIfNeededAsync()   ← Core
  ├── IsRemoteModeConfigured() / IsLocalModelUsable()                              ← Core statics
  ├── new EcaCompositionRoot(userConfigDir, args).Build()                          ← Core
  └── new AppController(GuiConsole, services…).RunAsync()                         ← TUI
```

> CLI args (including `--port <N>`) are forwarded to `EcaCompositionRoot(userConfigDir, args)`, which parses them via `AgentConfigBuilder`. `--port` routes to `UseLocalLLM(port:)` and is ultimately passed to the spawned LLM server by `ServerLauncher`.

## Packaging (NuGet, no embedded DLLs)

- `dotnet tool install -g ECAssistant.Console` → ~2.8 MB thin tool
- All libraries flow as PackageReferences (Core, TUI, transitive LLM launcher DLLs only — no server content)
- The ~170 MB LLM server is fetched by the wizard from nuget.org at first local setup — never embedded, never downloaded during chat
- Publish: tag `console-v*` → CI → GitHub Packages + nuget.org (OIDC trusted publishing)

## Why It's Separate

ECAssistantTUI is a **library** — hostable by any .NET 8 app. Other hosts (Avalonia, web) reference Core + TUI, implement `IGuiConsole`, create `AppController`, call `RunAsync()`.

## Build Order (reverse dependency order)

```
1. ECAssistantLLM   → ECAssistant.LLM.Server package
2. ECAssistantCore  → ECAssistant.Core package (refs LLM.Server)
3. ECAssistantTUI   → ECAssistant.TUI package (refs Core)
4. ECAssistantConsole → ecassistant tool (refs Core + TUI)
```

Restore needs `GITHUB_PACKAGES_TOKEN` (public GitHub Packages still requires auth).

## CLI Arguments

| Arg | Description |
|---|---|
| `--test` | Run automated test suite |
| `--mock` | Use mock engine (no model needed) |
| `--model <path>` | Override model path |
| `--ctx <n>` | Context size |
| `--port <N>` | LLM server port (routes to `UseLocalLLM(port:)` → `ServerLauncher --port`) |
| `--temp <f>` | Temperature |
| `--filter <name>` | Test filter prefix |
| `--verbose` / `-v` | Verbose test output |

## Changelog

- 2026-09-18: Setup/ folder removed — `FirstRunOrchestrator`/`ServerInstallCoordinator`/`NuGetServerFetcher` live in Core; tool package thinned 170 MB → 2.8 MB (v1.0.2); lib/ DLL sync retired long ago — all deps via GitHub Packages
- 2026-09-02: thin launcher, NuGet packages flow

## Addendum — strict dependency chain (1.0.6)

**Rule (Emre):** Console → TUI → Core. The Console must never reference Core
directly — TUI is the single dependency, so a stale TUI can never hide behind a
direct Core pin.

**Mechanics:** all Core touchpoints moved behind
`ECAssistant.TUI.Hosting.TuiAppHost` (first-run setup, remote/local checks,
composition root → AppController). The Console main csproj references only
`ECAssistant.TUI` (Core flows transitively, incl. DataProtection for SecureKeyStore).

**`ecassistant --test`:** the test host uses `Core.Testing` (a dev package) — it
moved to the dev test suite (`Tests/ConsoleTestHost.cs`), out of the shipped tool.
The `--test` CLI flag no longer exists in releases.

## Addendum — dependency chain rule (1.0.6)

**Rule (Emre):** Console → TUI → Core as a pure package chain. The Console main
csproj references `ECAssistant.TUI` ONLY; Core arrives transitively and is used
directly in code — nothing hidden or wrapped. This guarantees the Console always
runs against the TUI's pinned Core version (no stale-Core escape hatch).

**`ecassistant --test`:** the test host uses `Core.Testing` (a dev package) — it
moved to the dev test suite (`Tests/ConsoleTestHost.cs`), out of the shipped tool.
The `--test` CLI flag no longer exists in releases. The dev test csproj may
reference Core/TestSupport directly (it is never packed, so the shipped chain is
unaffected).

## Changelog — 2026-09-21 (terminal restore on all exit paths)

- `ConsoleApplication.RunAsync` wraps the `AppController.RunAsync` call in try/finally:
  `controller.ShutdownTerminal()` runs even when init fails (return 1) or an exception
  escapes — the terminal can no longer be left with alt-screen/cursor state leaked
  into the next launch.
- Companion fix in ECAssistantTUI (`GuiConsole.RestoreTerminal`, idempotent) and
  ECAssistantCore (`ServerConnection` probe timeout 5s → 12s — remote model-list
  probes observed >10s on OpenRouter, falsely reporting "endpoint unreachable").
