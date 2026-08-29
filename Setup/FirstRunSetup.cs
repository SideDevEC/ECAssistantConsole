using System.Net.Http;
using System.Text.Json;
using ECAssistant.Core.Setup;

namespace ECAssistantConsole;

/// <summary>
/// First-run installer and local-model readiness checks for the console host.
/// When no models are detected, lists the editable catalog (model-catalog.json)
/// and downloads the user's picks from HuggingFace, wiring llm-server.json automatically.
/// </summary>
internal sealed class FirstRunSetup
{
    private readonly string _userConfigDir;

    public FirstRunSetup(string userConfigDir)
    {
        _userConfigDir = userConfigDir ?? throw new ArgumentNullException(nameof(userConfigDir));
    }

    /// <summary>Runs setup when configs are missing or resolve to no usable model/provider.</summary>
    public async Task RunIfNeededAsync()
    {
        try
        {
            EnsureRuntimeDirectories();

            var catalogPath = Path.Combine(_userConfigDir, "model-catalog.json");
            var catalog = ModelCatalogDocument.Load(catalogPath);
            var validationError = catalog.Validate();
            if (validationError != null)
            {
                Console.WriteLine($"[Setup] model-catalog.json is invalid: {validationError} — skipping setup.");
                return;
            }

            var appsettingsPath = Path.Combine(_userConfigDir, "appsettings.json");
            var serverConfigPath = Path.Combine(_userConfigDir, "llm", "llm-server.json");
            await RunSetupIfNeededAsync(appsettingsPath, serverConfigPath, catalog, catalogPath);
        }
        // Deliberate boundary: first-run setup must never block application startup.
        catch (Exception ex) when (ex is IOException or JsonException or HttpRequestException or InvalidOperationException)
        {
            Console.WriteLine($"[Setup] First-run setup skipped: {ex.Message}");
        }
    }

    /// <summary>Interactive setup flow; no-op when appsettings/llm-server already resolve to a usable local model or remote provider.</summary>
    private async Task RunSetupIfNeededAsync(
        string appsettingsPath, string serverConfigPath, ModelCatalogDocument catalog, string catalogPath)
    {
        var detector = new FirstRunDetector(Path.Combine(_userConfigDir, "llm", "models"), serverConfigPath);
        var status = detector.Evaluate(catalog.Models);

        var remoteConfigured = false;
        if (File.Exists(appsettingsPath))
        {
            var appsettings = File.ReadAllText(appsettingsPath);
            remoteConfigured = appsettings.Contains("\"mode\": \"remote\"") &&
                               appsettings.Contains("\"llm_providers\"") &&
                               appsettings.Contains("\"endpoint\"");
        }

        var localUsable = IsLocalModelUsable(appsettingsPath, serverConfigPath);
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

        await SelectAndInstallAsync(appsettingsPath, selectable);
    }

    /// <summary>Prompts for categories/picks and downloads the chosen local models.</summary>
    private async Task SelectAndInstallAsync(string appsettingsPath, IReadOnlyList<ModelCatalogEntry> selectable)
    {
        Console.Write("Enable vision (image understanding)? [Y/n]: ");
        var visionEnabled = (Console.ReadLine()?.Trim() ?? "").ToLowerInvariant() != "n";
        var groups = visionEnabled
            ? new[] { CatalogModelCategory.Vision, CatalogModelCategory.Embedding }
            : new[] { CatalogModelCategory.Chat, CatalogModelCategory.Embedding };

        var flat = new List<ModelCatalogEntry>();
        foreach (var group in groups)
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
            await RemoteProviderSetup.RunInteractiveAsync(appsettingsPath);
            return; // Remote configured — no downloads needed.
        }

        Console.Write("Numbers to install (e.g. 1,3 / 'a' = all ★ / Enter = skip): ");
        var picks = ParsePicks(Console.ReadLine()?.Trim() ?? "", flat);
        if (picks.Count == 0) return;

        var appsettings = Path.Combine(_userConfigDir, "appsettings.json");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ECAssistant-Installer/1.0");
        var installer = new ModelInstallerService(
            http, Path.Combine(_userConfigDir, "llm", "models"),
            Path.Combine(_userConfigDir, "llm", "llm-server.json"), appsettings);

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

    private static IReadOnlyList<int> ParsePicks(string input, IReadOnlyList<ModelCatalogEntry> flat)
    {
        if (input.Length == 0) return Array.Empty<int>();

        if (input.Equals("a", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(0, flat.Count).Where(i => flat[i].Recommended).Select(i => i + 1).ToList();

        var picks = new List<int>();
        foreach (var token in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(token, out var n) && n >= 1 && n <= flat.Count && !picks.Contains(n))
                picks.Add(n);
        return picks;
    }

    private void EnsureRuntimeDirectories()
    {
        Directory.CreateDirectory(_userConfigDir);

        var appsettingsPath = Path.Combine(_userConfigDir, "appsettings.json");
        // Incorrect configs → quarantine and regenerate (installation will redo them)
        if (File.Exists(appsettingsPath))
        {
            try { JsonDocument.Parse(File.ReadAllText(appsettingsPath)); }
            catch (JsonException)
            {
                var backup = appsettingsPath + ".broken." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Move(appsettingsPath, backup);
                Console.WriteLine($"[Setup] appsettings.json is invalid — backed up to {Path.GetFileName(backup)}, regenerating.");
            }
        }

        Directory.CreateDirectory(Path.Combine(_userConfigDir, "llm", "models"));
    }

    /// <summary>Local mode is usable when llm.model_path exists, or llm-server.json references an existing model file.</summary>
    internal static bool IsLocalModelUsable(string appsettingsPath, string serverConfigPath)
    {
        try
        {
            if (File.Exists(appsettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
                if (doc.RootElement.TryGetProperty("llm", out var llm) &&
                    llm.TryGetProperty("model_path", out var mp) &&
                    mp.ValueKind == JsonValueKind.String &&
                    File.Exists(mp.GetString() ?? ""))
                    return true;
            }
        }
        catch (JsonException) { /* malformed handled earlier → not usable */ }
        catch (IOException) { /* unreadable → not usable */ }

        try
        {
            if (File.Exists(serverConfigPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(serverConfigPath));
                if (doc.RootElement.TryGetProperty("models", out var models))
                    foreach (var m in models.EnumerateArray())
                        if (m.TryGetProperty("path", out var p) &&
                            p.ValueKind == JsonValueKind.String &&
                            File.Exists(p.GetString() ?? ""))
                            return true;
            }
        }
        catch (JsonException) { /* malformed server config → not usable */ }
        catch (IOException) { /* unreadable → not usable */ }

        return false;
    }
}
