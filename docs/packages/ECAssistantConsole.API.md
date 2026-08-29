# ECAssistantConsole.API.md

Types: 10  |  LOC: 797  |  ~413 tokens

---

### Interface: IRemoteModelProbe
> A model advertised by a remote OpenAI-compatible /models endpoint.
Methods:
  - Task<RemoteProbeResult> ProbeAsync(string endpoint, string? apiKey, CancellationToken ct = default)

### Interface: ISetupUi
> Console I/O abstraction for the setup wizard — enables unit testing of flow logic.
Methods:
  - void WriteLine(string text = "")
  - void Write(string text)
  - string? ReadLine()

### Class: ConsoleSetupUi
> Default <see cref="ISetupUi"/> backed by System.Console.
Implements: ISetupUi

### Class: RemoteModelProbe
> Default <see cref="IRemoteModelProbe"/>: GET {endpoint}/models with optional bearer
Implements: IRemoteModelProbe
Constructor:
  - RemoteModelProbe(HttpClient? httpClient = null)

### Class: RemoteModelProbeTests
Cross-package deps: ECAssistantConsole.Setup, Xunit

### Class: SetupWizard
> Paths and services the wizard needs; assembled by the host.
Constructor:
  - SetupWizard(ISetupUi ui)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup

### Class: SetupWizardPickTests
Cross-package deps: ECAssistant.Core.Setup, ECAssistantConsole.Setup, Xunit

### Class: WizardContext
> Paths and services the wizard needs; assembled by the host.
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup

### Record: RemoteModelInfo
> A model advertised by a remote OpenAI-compatible /models endpoint.
Constructor:
  - RemoteModelInfo(string Id, bool SupportsVision)

### Record: RemoteProbeResult
> A model advertised by a remote OpenAI-compatible /models endpoint.
Constructor:
  - RemoteProbeResult(bool Reachable, IReadOnlyList<RemoteModelInfo> Models, string? Error = null)
