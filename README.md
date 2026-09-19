# ECAssistant Console

> **ECAssistant in your terminal, five minutes from now.** The assistant-first CLI: a local-first AI agent with tools, memory, sub-agents, and vision — running entirely on your machine. No cloud, no API keys.

[![NuGet](https://img.shields.io/nuget/v/ECAssistant.Console)](https://www.nuget.org/packages/ECAssistant.Console)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

We named it ECAssistant because we believe AI is there to **assist** people — and the Console is that promise in its most direct form: a colleague in your terminal that reads files, runs shell commands, searches the web, and shows you everything it does. Every tool call is permission-gated (approve / always / never). You stay in the loop, always.

Part of [ECAssistant](https://github.com/SideDevEC/ECAssistant) — your models, your keys, your machine. MIT.

## Install

Public on nuget.org — no token, no auth:

```bash
dotnet tool install -g ECAssistant.Console
ecassistant
```

(Or clone and `dotnet run` for a source checkout.)

## First run

The setup wizard walks you through everything:

1. **Choose local or remote** — local GGUF models, or any OpenAI-compatible endpoint. Only what you pick is ever downloaded.
2. **Model selection** — pick from the built-in catalog (chat, vision, embedding models), downloaded with SHA-256 verification.
3. **LLM server install** (local mode) — server binaries come from the NuGet package; the wizard stages them to `~/.ECAssistantLLM/server/`.
4. **Backend runtimes** — ternary models (e.g. Bonsai/Qwen3.8-27B) get their required llama.cpp runtime installed automatically.

After setup, nothing is downloaded at chat time — it's a finished, offline-capable product.

## What you get

- 🧠 **Tools** — files, shell, git, dotnet, web search/fetch, code editing, sub-agents. All permission-gated.
- 🖥️ **Terminal UI** — streaming chat, session tabs, tool-call rendering (powered by [ECAssistant.TUI](https://github.com/SideDevEC/ECAssistantTUI)).
- 👁️ **Vision** — send images to multimodal models.
- 🔒 **Local-first** — everything runs on your machine; nothing phones home.

## Configuration

- `appsettings.json` — app-level settings (written by the wizard)
- `~/.ECAssistantLLM/llm-server.json` — LLM server models and endpoints (managed by the wizard)

## Build your own host

The Console is deliberately thin — **~100 lines of wiring** around [ECAssistantCore](https://github.com/SideDevEC/ECAssistantCore). Read `ConsoleApplication.cs` as the reference for embedding an agent in *your* app.

## The ecosystem

| Repo | What it is |
|---|---|
| [ECAssistant](https://github.com/SideDevEC/ECAssistant) | Start here — overview & docs |
| [ECAssistantCore](https://github.com/SideDevEC/ECAssistantCore) | The embeddable agent library |
| [ECAssistantLLM](https://github.com/SideDevEC/ECAssistantLLM) | OpenAI-compatible local LLM server |
| [ECAssistantTUI](https://github.com/SideDevEC/ECAssistantTUI) | Terminal UI library |

## License

[MIT](LICENSE) — © 2026 SideDevEC
