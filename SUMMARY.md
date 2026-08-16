# ECAssistant Console — Summary

**Updated:** 2026-08-16 (v11.1)
**Build:** 0 errors, 0 warnings
**Tests:** 5/5 mock tests passing
**Repo:** https://github.com/LLamaDudeX/ECAssistantConsole.git
**Namespace:** `ECAssistantConsole`

## What It Is

The standalone console launcher for ECAssistant — a .NET 8 executable that wires `ECAssistant.TUI.dll` and `ECAssistant.Core.dll` together with `EGuiConsole` and `AppController`. This is what you run to use ECAssistant as a normal terminal application.

## Project Structure

```
ECAssistantConsole/
├── ECAssistantConsole.csproj      ← Console exe, AssemblyName=ecassistant
├── Program.cs                     ← Config loading, model resolution, AppController launch
└── .gitignore                     ← bin/, obj/
```

## Dependencies

- **ECAssistant.TUI** (project reference) — TUI library (AppController, EGuiConsole, layers)
- **ECAssistant.Core** (project reference) — engine (AgentConfigBuilder, Logger, tests)
- **Standard .NET 8**

## Build & Run

```bash
# Build all three in order
cd ~/Agent/ECAssistant/ECAssistantCore && dotnet build
cd ~/Agent/ECAssistant/ECAssistantTUI && dotnet build
cd ~/Agent/ECAssistant/ECAssistantConsole && dotnet build

# Run with a model
dotnet run -- --model /path/to/model.gguf

# Run mock tests (no model needed)
dotnet run -- --test --mock

# CLI args
#   --model <path>       Override model path
#   --ctx <n>            Context size
#   --gpu <n>            GPU layers
#   --threads <n>        Thread count
#   --temp <f>           Temperature
```

## What Program.cs Does

1. Parses CLI args (`--test`, `--mock`, `--model`, etc.)
2. Builds `EAgentConfig` via `AgentConfigBuilder`
3. Resolves model path (relative → absolute)
4. Creates directories (`~/ECAssistant/`, Memory, Workspace)
5. Creates `EGuiConsole` + `AppController` + calls `RunAsync()`

No external tools are passed — standalone ECAssistant uses only built-in Core tools.

## Relationship to Other Projects

```
~/Agent/ECAssistant/
  ├── ECAssistantCore/       ← Core engine library (DLL)
  ├── ECAssistantTUI/        ← TUI library (DLL)
  └── ECAssistantConsole/    ← This project — console launcher (Exe)
```

The console is the simplest entry point. Future hosts (ECSQL, etc.) would reference TUI + Core directly and provide their own `IGuiConsole` implementation.