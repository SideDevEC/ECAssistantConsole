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

        if (!File.Exists(services.ModelPath))
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
            var llmRoot = Path.Combine(userConfigDir, "llm");
            var modelsDir = Path.Combine(llmRoot, "models");
            var catalogPath = Path.Combine(userConfigDir, "model-catalog.json");
            var serverConfigPath = Path.Combine(llmRoot, "llm-server.json");

            var catalog = ModelCatalogDocument.Load(catalogPath);
            var validationError = catalog.Validate();
            if (validationError != null)
            {
                Console.WriteLine($"[Setup] model-catalog.json is invalid: {validationError} — skipping setup.");
                return;
            }

            var detector = new FirstRunDetector(modelsDir, serverConfigPath);
            var status = detector.Evaluate(catalog.Models);
            if (!status.NeedsSetup) return;

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
            var installer = new ModelInstallerService(http, modelsDir, serverConfigPath);

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