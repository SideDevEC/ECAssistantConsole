# ECAssistant Console — Architecture

**Updated:** 2026-08-26 (v12.0 — build script, root-based config, fresh DLL sync)
**Build:** 0 errors, 0 warnings
**Tests:** 5/5 mock tests passing

## Overview

ECAssistantConsole is a minimal .NET 8 console executable that launches ECAssistant's TUI in a terminal. It is the simplest possible entry point — config loading, model resolution, and controller creation. No application logic lives here.

## Project Structure

```
ECAssistantConsole/
├── ECAssistantConsole.csproj      ← Console exe
│     OutputType=Exe, AssemblyName=ecassistant
│     Packages: Microsoft.Extensions.Logging.Abstractions + System.Text.Json (LLamaSharp removed in v11.2)
│
└── Program.cs                     ← Entry point
```

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