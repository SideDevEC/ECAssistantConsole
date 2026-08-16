# ECAssistant Console — Architecture

**Updated:** 2026-08-16 (v11.1)
**Build:** 0 errors, 0 warnings
**Tests:** 5/5 mock tests passing

## Overview

ECAssistantConsole is a minimal .NET 8 console executable that launches ECAssistant's TUI in a terminal. It is the simplest possible entry point — config loading, model resolution, and controller creation. No application logic lives here.

## Project Structure

```
ECAssistantConsole/
├── ECAssistantConsole.csproj      ← Console exe
│     OutputType=Exe, AssemblyName=ecassistant
│     ProjectReferences: ECAssistant.TUI, ECAssistant.Core
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
| `--gpu <n>` | GPU layers |
| `--threads <n>` | Thread count |
| `--temp <f>` | Temperature |
| `--filter <name>` | Test filter prefix |
| `--verbose` / `-v` | Verbose test output |