using ECAssistant.Core;
using ECAssistant.Core.Composition;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Setup;
using ECAssistant.TUI.Controller;
using ECAssistant.Core.Services;
using ECAssistant.Core.Testing;
using ECAssistant.TUI.UI;

namespace ECAssistantConsole;

public class Program
{
    [System.STAThread]
    public static async Task<int> Main(string[] args)
    {
        // ── Test mode: run automated tests ──
        if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
        {
            return await RunTestsAsync(args.Skip(1).ToArray());
        }

        // ── Composition root: wires all services ──
        var userConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ECAssistant");

        // First-run: offer catalog downloads when no models exist yet (Build throws otherwise)
        await RunFirstRunSetupIfNeededAsync(userConfigDir);

        // Local mode with no model file → friendly exit (Build would print a scary diagnostic box)
        var appsettings = File.Exists(Path.Combine(userConfigDir, "appsettings.json"))
            ? File.ReadAllText(Path.Combine(userConfigDir, "appsettings.json")) : "";
        var remoteMode = appsettings.Contains("\"mode\": \"remote\"");
        if (!remoteMode)
        {
            // A GGUF on disk alone is NOT enough — config must resolve to it
            var usable = LocalModelUsable(
                Path.Combine(userConfigDir, "appsettings.json"),
                Path.Combine(userConfigDir, "llm", "llm-server.json"));
            if (!usable)
            {
                Console.WriteLine("[Setup] No local model installed yet.");
                Console.WriteLine("[Hint] Run again and pick models from the catalog (or choose remote AI),");
                Console.WriteLine($"       or place a .gguf in {Path.Combine(userConfigDir, "llm", "models")}.");
                return 1;
            }
        }

        var root = new EcaCompositionRoot(userConfigDir, args);
        EcaServiceBundle services;
        try
        {
            services = root.Build();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Startup failed: {ex.Message}");
            Console.WriteLine($"[Hint] Run again to pick a model from the catalog, or edit model-catalog.json in {userConfigDir}.");
            return 1;
        }

        if (!File.Exists(services.ModelPath) && !services.Config.LlmProvider.IsRemote)
        {
            Console.WriteLine($"[Error] Model not found: {services.ModelPath}");
            Console.WriteLine($"[Hint] Put your .gguf model in: {userConfigDir} or set full path in appsettings.json");
            return 1;
        }

        // ── Create terminal and controller ──
        var console = new EGuiConsole();
        var controller = new AppController(
            console,
            services.Config,
            services.ModelPath,
            services.WorkingDirectory,
            services.UserConfigDirectory,
            services.Logger,
            null,
            services.BackgroundProcesses,
            services.FileWatcher);

        return await controller.RunAsync();
    }

    // ── Test Mode ──

    /// <summary>
    /// First-run installer: when no models are detected, list the editable catalog
    /// (model-catalog.json) and download the user's picks from HuggingFace, wiring
    /// llm-server.json automatically.
    /// </summary>
    private static async Task RunFirstRunSetupIfNeededAsync(string userConfigDir)
    {
        try
        {
            // Create the runtime folder structure up front — a fresh install has nothing
            Directory.CreateDirectory(userConfigDir);
            var appsettingsPath = Path.Combine(userConfigDir, "appsettings.json");
            // Incorrect configs → quarantine and regenerate (installation will redo them)
            if (File.Exists(appsettingsPath))
            {
                try { System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettingsPath)); }
                catch (System.Text.Json.JsonException)
                {
                    var backup = appsettingsPath + ".broken." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    File.Move(appsettingsPath, backup);
                    Console.WriteLine($"[Setup] appsettings.json is invalid — backed up to {Path.GetFileName(backup)}, regenerating.");
                }
            }
            var llmRoot = Path.Combine(userConfigDir, "llm");
            var modelsDir = Path.Combine(llmRoot, "models");
            Directory.CreateDirectory(modelsDir);
            var catalogPath = Path.Combine(userConfigDir, "model-catalog.json");
            var serverConfigPath = Path.Combine(llmRoot, "llm-server.json");

            var catalog = ModelCatalogDocument.Load(catalogPath);
            var validationError = catalog.Validate();
            if (validationError != null)
            {
                Console.WriteLine($"[Setup] model-catalog.json is invalid: {validationError} — skipping setup.");
                return;
            }

            // Installation starts when: no models/providers configured (detector),
            // OR configs exist but don't resolve to a usable local model.
            var detector = new FirstRunDetector(modelsDir, serverConfigPath);
            var status = detector.Evaluate(catalog.Models);

            var remoteConfigured = false;
            if (File.Exists(appsettingsPath))
            {
                var appsettings = File.ReadAllText(appsettingsPath);
                remoteConfigured = appsettings.Contains("\"mode\": \"remote\"") &&
                                   appsettings.Contains("\"llm_providers\"") &&
                                   appsettings.Contains("\"endpoint\"");
            }
            var localUsable = LocalModelUsable(appsettingsPath, serverConfigPath);
            if (!status.NeedsSetup && (remoteConfigured || localUsable)) return;

            if (!remoteConfigured && !localUsable && !status.NeedsSetup)
                Console.WriteLine("[Setup] Config exists but no usable model or provider found — running installation.");


            Console.WriteLine();
            Console.WriteLine("════════ First-Run Setup — no models detected ════════");
            Console.WriteLine($"Model catalog: {catalogPath} (edit anytime to add your own)");
            Console.WriteLine();

            var selectable = catalog.Models
                .Where(m => !status.InstalledEntryIds.Contains(m.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (selectable.Count == 0) return;

            var flat = new List<ModelCatalogEntry>();
            foreach (var group in new[] { CatalogModelCategory.Chat, CatalogModelCategory.Vision, CatalogModelCategory.Embedding })
            {
                var entries = selectable.Where(m => m.Category == group).ToList();
                if (entries.Count == 0) continue;
                Console.WriteLine($"── {group} ──");
                foreach (var m in entries)
                {
                    flat.Add(m);
                    Console.WriteLine($"  [{flat.Count}] {m.DisplayName}{(m.Recommended ? " ★" : "")}  ({m.TotalSizeGb:0.##} GB) — {m.Notes}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("How should ECAssistant run its AI?");
            Console.WriteLine("  [1] Local models  (GGUF on this machine — downloaded below)");
            Console.WriteLine("  [2] Remote AI     (OpenAI-compatible API: OpenAI, OpenRouter, Ollama cloud, …)");
            Console.Write("Choose [1/2, Enter = 1]: ");
            if ((Console.ReadLine()?.Trim() ?? "") == "2")
            {
                await RunRemoteSetupAsync(appsettingsPath);
                return; // Remote configured — no downloads needed.
            }
            Console.Write("Numbers to install (e.g. 1,3 / 'a' = all ★ / Enter = skip): ");
            var input = Console.ReadLine()?.Trim() ?? "";
            if (input.Length == 0) return;

            var picks = new List<int>();
            if (input.Equals("a", StringComparison.OrdinalIgnoreCase))
                picks = flat.Select((m, i) => (m, i)).Where(t => t.m.Recommended).Select(t => t.i + 1).ToList();
            else
                foreach (var token in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (int.TryParse(token, out var n) && n >= 1 && n <= flat.Count && !picks.Contains(n)) picks.Add(n);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ECAssistant-Installer/1.0");
            var installer = new ModelInstallerService(http, modelsDir, serverConfigPath, appsettingsPath);

            foreach (var idx in picks)
            {
                var entry = flat[idx - 1];
                Console.WriteLine($"▼ Downloading {entry.DisplayName} ({entry.TotalSizeGb:0.##} GB)");
                var result = await installer.InstallAsync(entry, p =>
                {
                    Console.Write($"\r  {p.Percent,5:0}%  {p.BytesReceived / 1048576.0:0} MB  {p.MbPerSecond:0.#} MB/s   ");
                });
                Console.WriteLine();
                Console.WriteLine(result.Success ? $"✔ {result.Message}" : $"✘ {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Setup] First-run setup skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Console first-run remote setup: prompts for OpenAI-compatible endpoint/key/model,
    /// verifies reachability, encrypts the key into the key store, saves config.
    /// </summary>
    private static async Task RunRemoteSetupAsync(string appsettingsPath)
    {
        Console.WriteLine();
        Console.WriteLine("── Remote AI setup ──");
        Console.Write("  Endpoint (e.g. https://api.openai.com/v1): ");
        var endpoint = Console.ReadLine()?.Trim() ?? "";
        if (endpoint.Length == 0) { Console.WriteLine("Skipped."); return; }

        Console.Write("  API key: ");
        var apiKey = Console.ReadLine()?.Trim() ?? "";

        Console.Write("  Model ID (e.g. gpt-4o-mini): ");
        var modelId = Console.ReadLine()?.Trim() ?? "";
        if (modelId.Length == 0) { Console.WriteLine("Skipped."); return; }

        Console.Write("  Embedding model ID [Enter = text-embedding-3-small]: ");
        var embeddingModelId = Console.ReadLine()?.Trim() ?? "";
        if (embeddingModelId.Length == 0) embeddingModelId = "text-embedding-3-small";

        Console.WriteLine("  Testing connection...");
        var reachable = await TestRemoteReachableAsync(endpoint, apiKey);
        if (!reachable)
        {
            Console.Write("  Connection failed — save anyway? [y/N]: ");
            var save = (Console.ReadLine()?.Trim() ?? "").ToLowerInvariant();
            if (save != "y" && save != "yes") { Console.WriteLine("Skipped."); return; }
        }
        else
        {
            Console.WriteLine("  ✔ Endpoint reachable.");
        }

        new RemoteProviderSetupWriter(appsettingsPath).Write(new RemoteProviderConfig
        {
            Name = new UriBuilder(endpoint).Host,
            Endpoint = endpoint,
            ApiKey = apiKey.Length > 0 ? apiKey : null,
            ModelId = modelId,
            EmbeddingModelId = embeddingModelId
        });
        Console.WriteLine($"✔ Remote AI configured: {modelId} @ {endpoint} (key encrypted to key store)");
    }

    private static async Task<bool> TestRemoteReachableAsync(string endpoint, string apiKey)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var resp = await http.GetAsync($"{endpoint.TrimEnd('/')}/models");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Local mode is usable when llm.model_path exists, or llm-server.json references an existing model file.</summary>
    private static bool LocalModelUsable(string appsettingsPath, string serverConfigPath)
    {
        try
        {
            if (File.Exists(appsettingsPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettingsPath));
                if (doc.RootElement.TryGetProperty("llm", out var llm) &&
                    llm.TryGetProperty("model_path", out var mp) &&
                    mp.ValueKind == System.Text.Json.JsonValueKind.String &&
                    File.Exists(mp.GetString() ?? ""))
                    return true;
            }
        }
        catch { /* malformed handled earlier */ }

        try
        {
            if (File.Exists(serverConfigPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(serverConfigPath));
                if (doc.RootElement.TryGetProperty("models", out var models))
                    foreach (var m in models.EnumerateArray())
                        if (m.TryGetProperty("path", out var p) &&
                            p.ValueKind == System.Text.Json.JsonValueKind.String &&
                            File.Exists(p.GetString() ?? ""))
                            return true;
            }
        }
        catch { /* malformed server config → not usable */ }

        return false;
    }

    /// <summary>Extract a simple "model_path": "..." value from the llm section of appsettings text (best effort).</summary>
    private static string? ExtractJsonString(string json, string property)
    {
        var idx = json.IndexOf($"\"{property}\"", StringComparison.Ordinal);
        if (idx < 0) return null;
        var colon = json.IndexOf(':', idx);
        if (colon < 0) return null;
        var quote1 = json.IndexOf('"', colon + 1);
        if (quote1 < 0) return null;
        var quote2 = json.IndexOf('"', quote1 + 1);
        if (quote2 < 0) return null;
        var value = json[(quote1 + 1)..quote2];
        return value.StartsWith("/") && File.Exists(value) ? value : null;
    }

    private static async Task<int> RunTestsAsync(string[] testArgs)
    {
        bool useMock = testArgs.Any(a => a.Equals("--mock", StringComparison.OrdinalIgnoreCase));

        var userConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ECAssistant");
        var config = AgentConfigBuilder.Create()
            .WorkingDirectory(userConfigDir)
            .Build();
        var modelPath = config.Llm.ModelPath;

        for (int i = 0; i < testArgs.Length; i++)
        {
            if (testArgs[i].Equals("--model", StringComparison.OrdinalIgnoreCase) && i + 1 < testArgs.Length)
                modelPath = testArgs[++i];
        }

        if (!useMock && (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath)))
        {
            Console.WriteLine($"❌ Model not found: {modelPath}");
            Console.WriteLine("   Set model path in ~/ECAssistant/appsettings.json or pass --model /path/to/model.gguf");
            Console.WriteLine("   Or use --mock for model-independent tests (no GGUF needed).");
            return 1;
        }

        string? filter = null;
        bool verbose = false;
        for (int i = 0; i < testArgs.Length; i++)
        {
            if (testArgs[i].Equals("--filter", StringComparison.OrdinalIgnoreCase) && i + 1 < testArgs.Length)
                filter = testArgs[++i];
            if (testArgs[i].Equals("--verbose", StringComparison.OrdinalIgnoreCase) || testArgs[i].Equals("-v", StringComparison.OrdinalIgnoreCase))
                verbose = true;
        }

        var ecaTests = new EcaTestSuite();
        var allTests = useMock ? ecaTests.MockScenarios : ecaTests.All;
        List<TestScenario> tests;
        if (!string.IsNullOrEmpty(filter))
        {
            tests = allTests.Where(t => t.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (tests.Count == 0)
            {
                Console.WriteLine($"No tests match filter '{filter}'. Available:");
                foreach (var t in allTests)
                    Console.WriteLine($"  {t.Name}");
                return 1;
            }
        }
        else
        {
            tests = allTests;
        }

        Console.WriteLine($"\n🧪 Running {tests.Count} test(s) with {(useMock ? "MOCK ENGINE (no model)" : $"model: {Path.GetFileName(modelPath)}")}");
        Console.WriteLine($"   Filter: {filter ?? "(all)"}\n");

        await using var runner = new TestRunner(useMock ? "/mock/model.gguf" : modelPath) { Verbose = verbose, UseMockEngine = useMock };
        var results = await runner.RunAllAsync(tests);

        var logPath = Path.Combine(runner.TestRootDir, "test_results.log");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ECAssistant Test Results — {DateTime.UtcNow:O}");
        sb.AppendLine($"Model: {(useMock ? "MOCK ENGINE" : modelPath)}");
        sb.AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine($"{(r.Passed ? "PASS" : "FAIL")} | {r.Name} | {r.Duration.TotalSeconds:F1}s | {r.FailureReason}");
            if (!r.Passed)
            {
                sb.AppendLine($"  Output: {r.FinalOutput}");
                sb.AppendLine();
            }
        }
        File.WriteAllText(logPath, sb.ToString());
        Console.WriteLine($"\n📝 Detailed log: {logPath}");

        return results.Any(r => !r.Passed) ? 1 : 0;
    }
}