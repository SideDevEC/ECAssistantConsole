# AGENTS.md — ECAssistantConsole (AI-consumable)

Compact orientation for AI agents working in this repo. Humans: read README.md.

## Identity
- **Package:** `ECAssistant.Console` v15.0.0 · net8.0 · **dotnet tool** `ecassistant`
- **Purpose:** The reference host / end-user CLI for ECAssistant — deliberately THIN (~100 lines of wiring in `ConsoleApplication.cs`).
- **Chain (strict):** Console → TUI → Core → LLM.Server. Console references TUI ONLY (never Core directly in the main csproj). When ANY upstream ships, ALL downstream bump + ship.

## Files that matter
| File | Role |
|---|---|
| `ConsoleApplication.cs` | THE reference for embedding an agent in your own app — full wiring example |
| `Program.cs` | Entry point (minimal) |
| `HostPaths.cs` | Config/data dir resolution |
| `Features/FirstRunSetup*` | Wizard: local/remote choice → model catalog → server install (version from Core's `ServerInstallCoordinator` — no separate const here) |
| `Tests/` | Dev-only suite. Never packed, never built in CI. Uses `EcaUseProjectRefs` split (project refs locally, package refs in CI) |

## What the wizard does (order matters)
1. Local or remote choice (only the choice is downloaded)
2. Model selection from catalog (chat/vision/embedding, SHA-256 verified)
3. Local mode only: LLM server install → `~/.ECAssistantLLM/server/` (from NuGet package)
4. Backend runtimes for special models (e.g. Bonsai ternary → llama.cpp runtime)
After setup: zero downloads at chat time.

## Build & run
```bash
export EcaUseProjectRefs=true     # local dev: sibling project refs
dotnet build ECAssistantConsole.csproj
dotnet run                         # launches the assistant

# publish (CI does this on tag; local check:)
dotnet pack -c Release             # thin tool ~2.8 MB — server is NEVER embedded
```

## Versioning & release
- LOCKSTEP version with all ECAssistant packages (15.0.0). References `ECAssistant.TUI` 15.0.0.
- Publish: tag `console-v15.0.0` (or manual dispatch with version input) → CI → GitHub Packages + nuget.org via OIDC trusted publishing.
- NEVER tag without Emre's explicit "ship it".
- Anonymity rule: public repos — no personal paths/names in code, commits, issues, or docs.

## Gotchas
- `RollForward=Major` on the tool — users on older .NET 8 runtimes still run it.
- FirstRunSetup comments say "keep LlmServerPackageVersion in sync" — the actual const lives in Core (`ServerInstallCoordinator.RequiredServerVersion`); there is intentionally NO second const here.
