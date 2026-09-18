# ECAssistant Console

**The terminal application** for [ECAssistant](https://github.com/SideDevEC/ECAssistantLLM) — a local-first AI agent with tools, memory, sub-agents, and vision, running entirely on your machine. No cloud, no API keys.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## First run

```bash
dotnet run
```

The setup wizard walks you through everything:

1. **LLM server install** — server binaries come from the NuGet package; the wizard stages them to `~/.ECAssistantLLM/server/`
2. **Model selection** — pick from the built-in catalog (chat, vision, embedding models), downloaded with SHA-256 verification
3. **Backend runtimes** — ternary models (e.g. Bonsai/Qwen3.8-27B) get their required llama.cpp runtime installed automatically

After setup, nothing is downloaded at chat time — it's a finished, offline-capable product.

## Configuration

- `appsettings.json` — app-level settings (written by the wizard)
- `~/.ECAssistantLLM/llm-server.json` — LLM server models and endpoints (managed by the wizard)

## Related repos

| Repo | What it is |
|---|---|
| [ECAssistantLLM](https://github.com/SideDevEC/ECAssistantLLM) | OpenAI-compatible local LLM server |
| [ECAssistantCore](https://github.com/SideDevEC/ECAssistantCore) | Agent engine, tools, memory, wizard |
| [ECAssistantTUI](https://github.com/SideDevEC/ECAssistantTUI) | Terminal UI library |

## License

[MIT](LICENSE) — © 2026 SideDevEC
