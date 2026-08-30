# ECAssistant Console — Architecture

**Updated:** 2026-08-30 (v12.11 — root-only runtime contract, Core-owned server config)
**Build:** 0 errors, 0 warnings
**Tests:** 8/8 tests passing

## Overview

ECAssistantConsole is a minimal .NET 8 console executable that launches ECAssistant's TUI in a terminal. It is the simplest possible entry point — config loading, model resolution, and controller creation. No application logic lives here.

## Project Structure

```
ECAssistantConsole/
├── ECAssistantConsole.csproj      ← Console exe
│     OutputType=Exe, AssemblyName=ecassistant
│     Packages: Microsoft.Extensions.Logging.Abstractions + System.Text.Json (LLamaSharp removed in v11.2)
│
├── Program.cs                     ← Entry point (--test → ConsoleTestHost, else ConsoleApplication)
├── ConsoleApplication.cs          ← Composition root wiring, startup guards
├── ConsoleTestHost.cs             ← --test harness host
└── Setup/
    ├── ISetupUi.cs                ← Console I/O abstraction (testable wizard)
    ├── ConsoleSetupUi.cs          ← System.Console implementation
    ├── FirstRunSetup.cs           ← Detects "setup needed", prepares dirs, launches wizard
    │                                (v12.11: config mode checks use real JSON parsing via
    │                                System.Text.Json — no more string-contains checks; handles
    │                                both llm_provider and llm_providers key shapes; remote-mode
    │                                detection covers provider mode "remote" written by the wizard)
    ├── SetupWizard.cs             ← Staged installer (see Wizard Flow below)
    ├── IRemoteModelProbe.cs       ← Remote /models probe contract
    └── RemoteModelProbe.cs        ← GET {endpoint}/models, vision auto-detection
└── Tests/                         ← ECAssistantConsole.Tests (xunit; excluded from app compile)
```

## Wizard Flow (v12.0 — staged installer)

Each stage shows only what it needs — no more wall-of-text:

1. **LLM stage** — "local or remote?"
   - Remote: endpoint → API key → probe `/models` → pick model from list → vision auto-detected via API (`architecture.input_modalities` / `modalities` / `capabilities.vision`); write via `RemoteProviderSetupWriter` (embedding id intentionally unset).
   - Local: vision? y/n → list Chat **or** Vision catalog entries (never embeddings) → download via `ModelInstallerService`.
2. **Embeddings/memory stage** — "enable memory embeddings?"
   - No → `EmbeddingSetupWriter.Disable()`.
   - Local → list Embedding catalog entries → download → `SetMode("local", modelId)`.
   - Remote → endpoint (default = AI provider's) + key (default = provider's, encrypted as `keyfile:embeddings.key`) → probe + pick → `SetMode("remote", …)`.
3. **Finish** — host proceeds normally: composition root → AppController → connect/session (server autostarts in local mode).

Core support added: `EmbeddingConfig.ApiKey` (supports `keyfile:` refs), `SessionBuilder.ResolveEmbeddingApiKey()`, remote-mode branch in `ResolveEmbeddingEndpoint/ModelId`, `EmbeddingSetupWriter.Disable()`.

## Dependency Flow

```
Program.cs
  │
  ├── AgentConfigBuilder.Create() → EAgentConfig
  ├── Resolve model path (relative → absolute)
  ├── Create directories
  ├── new EGuiConsole()                    ← from ECAssistant.TUI
  ├── new AppController(console, config, ...)  ← from ECAssistant.TUI
  └── await controller.RunAsync()
```

> CLI args (including `--port <N>`) are forwarded to `EcaCompositionRoot(userConfigDir, args)`, which parses them via `AgentConfigBuilder`. `--port` routes to `UseLocalLLM(port:)` (Core v10.31) and is ultimately passed to the spawned LLM server by `ServerLauncher`.

## Why It's Separate

ECAssistantTUI is a **library** — it can be hosted by any .NET 8 app. The console project is the default host for standalone terminal use. Other hosts (ECSQL with Avalonia, a web-based host, etc.) would:

1. Reference ECAssistant.TUI + ECAssistant.Core
2. Implement `IGuiConsole` with their own rendering backend
3. Create `AppController` with their `IGuiConsole` + external tools
4. Call `RunAsync()`

## Build Order

```
1. ECAssistantCore → ECAssistant.Core.dll
2. ECAssistantTUI  → ECAssistant.TUI.dll (references Core)
3. ECAssistantConsole → ecassistant (references both)
```

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

> `--gpu` / `--threads` are server-side (LLM server `llm-server.json`) since v11.2 — not parsed by the console.

## DLL Sync (v11.6)

Console references Core + TUI as pre-built DLLs from `lib/`. After building Core/TUI, DLLs must be copied to **both**:
- `ECAssistantConsole/lib/*.dll` (compile-time reference)
- `ECAssistantConsole/bin/Debug/net8.0/*.dll` (runtime copy — `--no-build` uses this)

Failing to copy to `bin/Debug` means `dotnet run --no-build` uses stale DLLs.
## Changelog — 2026-08-27

- build.sh → forwards to repo-root script (per-project standalone copies were path-broken)
