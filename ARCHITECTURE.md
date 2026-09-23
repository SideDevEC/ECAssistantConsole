# ECAssistant Console — Architecture (as-is)

**Updated:** 2026-09-23 · **Build:** 0 errors
**History:** git log — this file describes the CURRENT state only.

## Overview

ECAssistantConsole is a minimal .NET 8 console executable — a **dumb launcher** for ECAssistant. It contains zero application logic: setup orchestration lives in Core (`FirstRunOrchestrator`), presentation lives in TUI (`AppController`). Console only dispatches, validates startup preconditions, and wires the composition root.

## Project Structure

```
ECAssistantConsole/
├── ECAssistantConsole.csproj      ← Console exe, PackAsTool (thin dotnet tool ~2.8 MB)
│     PackageId=ECAssistant.Console; deps: TUI (Core flows transitively)
│     NO ECAssistant.LLM.Server content reference — the server is NOT embedded
├── Program.cs                     ← Entry point (--test → ConsoleTestHost, else ConsoleApplication)
├── ConsoleApplication.cs          ← Thin host: FirstRunOrchestrator → composition root → AppController
├── ConsoleTestHost.cs             ← --test harness host
├── HostPaths.cs                   ← User config dir resolution
└── Tests/                         ← ECAssistantConsole.Tests (xunit)
```

## Startup Flow

1. `FirstRunOrchestrator.RunIfNeededAsync()` (Core) — shared with TUI; wizard-time server install, pure-remote users get zero LLM footprint
2. Validate: remote configured OR local model usable (Core statics `IsRemoteProviderConfigured` / `IsLocalModelUsable`)
3. `EcaCompositionRoot(userConfigDir, args).Build()` → TUI `AppController(GuiConsole, services…).RunAsync()`

## Packaging

- `dotnet tool install -g ECAssistant.Console` → ~2.8 MB thin tool
- All libraries flow as PackageReferences; the ~170 MB LLM server is fetched by the wizard from nuget.org at first local setup — never embedded
- Publish: tag `console-v*` → CI → GitHub Packages + nuget.org (OIDC trusted publishing)

## Dependency Chain Law

Console references ONLY TUI. Ship order (one at a time, each confirmed indexed before the next): TestSupport → LLM → Core → TUI → Console. NEVER tag downstream packages while upstream isn't live on nuget.org. Bump `PackageReference` versions in the same commit as the release version bump.

## CLI Arguments

| Arg | Description |
|---|---|
| `--test` | Run automated test suite |
| `--mock` | Use mock engine (no model needed) |
| `--model <path>` | Override model path |
| `--ctx <n>` | Context size |
| `--port <N>` | LLM server port → `UseLocalLLM(port:)` → `ServerLauncher --port` |
| `--temp <f>` | Temperature |
| `--filter <name>` | Test filter prefix |
| `--verbose` / `-v` | Verbose test output |