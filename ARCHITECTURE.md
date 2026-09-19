# ECAssistant Console — Architecture

**Updated:** 2026-09-19 (v1.0.3 — Core 12.9.5 with hardware-adaptive catalog: Bonsai default + Qwen3.5-4B light + Qwen3.6-35B max, all vision-capable; LLM server 14.9.2)
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
  └── new AppController(EGuiConsole, services…).RunAsync()                         ← TUI
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
