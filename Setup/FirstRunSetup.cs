using System.Net.Http;
using System.Text.Json;
using ECAssistant.Core.Setup;
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

        var wizard = new SetupWizard(new ConsoleSetupUi());
        await wizard.RunAsync(new WizardContext
        {
            AppsettingsPath = appsettingsPath,
            UserConfigDir = _userConfigDir,
            Catalog = catalog,
            InstalledEntryIds = status.InstalledEntryIds,
            Installer = CreateInstaller(),
            Probe = new RemoteModelProbe()
        });
    }

    private ModelInstallerService CreateInstaller()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ECAssistant-Installer/1.0");
        return new ModelInstallerService(
            http, Path.Combine(_userConfigDir, "llm", "models"),
            Path.Combine(_userConfigDir, "llm", "llm-server.json"),
            Path.Combine(_userConfigDir, "appsettings.json"));
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
