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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or InvalidOperationException or OperationCanceledException)
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

        var remoteConfigured = File.Exists(appsettingsPath) && IsRemoteProviderConfigured(appsettingsPath);

        var localUsable = IsLocalModelUsable(appsettingsPath, serverConfigPath);
        if (!status.NeedsSetup && (remoteConfigured || localUsable)) return;

        // Reaching this point means either setup is needed or no usable provider/model was
        // found, so when setup is NOT needed the config exists but resolves to nothing usable.
        if (!status.NeedsSetup)
            Console.WriteLine("[Setup] Config exists but no usable model or provider found — running installation.");

        // HttpClient is owned by this scope and disposed after the wizard completes;
        // ModelInstallerService uses it for the whole download flow.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ECAssistant-Installer/1.0");
        var installer = new ModelInstallerService(
            http, Path.Combine(_userConfigDir, "llm", "models"),
            Path.Combine(_userConfigDir, "llm", "llm-server.json"),
            Path.Combine(_userConfigDir, "appsettings.json"));

        var wizard = new SetupWizard(new ConsoleSetupUi());
        await wizard.RunAsync(new WizardContext
        {
            AppsettingsPath = appsettingsPath,
            UserConfigDir = _userConfigDir,
            Catalog = catalog,
            InstalledEntryIds = status.InstalledEntryIds,
            Installer = installer,
            Probe = new RemoteModelProbe(),
            ModelsDir = Path.Combine(_userConfigDir, "llm", "models")
        });
    }

    /// <summary>
    /// True when appsettings.json configures a usable remote provider.
    /// Matches what SetupWizard/RemoteProviderSetupWriter writes: an
    /// llm_provider section with mode="remote" and an endpoint, plus a
    /// non-empty llm_providers section (either key is sufficient if only one
    /// is present, since the writer always emits both).
    /// </summary>
    internal static bool IsRemoteProviderConfigured(string appsettingsPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            bool hasRemoteMode = false, hasEndpoint = false, hasProviders = false;

            if (root.TryGetProperty("llm_provider", out var llm) && llm.ValueKind == JsonValueKind.Object)
            {
                hasRemoteMode = llm.TryGetProperty("mode", out var mode) &&
                                mode.ValueKind == JsonValueKind.String &&
                                string.Equals(mode.GetString(), "remote", StringComparison.OrdinalIgnoreCase);
                hasEndpoint = llm.TryGetProperty("endpoint", out var endpoint) &&
                              endpoint.ValueKind == JsonValueKind.String &&
                              !string.IsNullOrWhiteSpace(endpoint.GetString());
            }

            if (root.TryGetProperty("llm_providers", out var providers) && providers.ValueKind == JsonValueKind.Object)
            {
                hasProviders =
                    (providers.TryGetProperty("default_provider", out var def) &&
                     def.ValueKind == JsonValueKind.String &&
                     !string.IsNullOrWhiteSpace(def.GetString())) ||
                    (providers.TryGetProperty("providers", out var list) &&
                     list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0);
            }

            return hasRemoteMode && hasEndpoint && hasProviders;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable/invalid config: treat as not configured; the wizard
            // (or quarantine in EnsureRuntimeDirectories) will handle it.
            return false;
        }
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
                // High-resolution suffix so two broken files in the same second never collide.
                var backup = appsettingsPath + ".broken." + DateTime.UtcNow.Ticks;
                File.Move(appsettingsPath, backup);
                Console.WriteLine($"[Setup] appsettings.json is invalid — backed up to {Path.GetFileName(backup)}, regenerating.");
            }
        }

        Directory.CreateDirectory(Path.Combine(_userConfigDir, "llm", "models"));
    }

    /// <param name="appsettingsPath">Path to appsettings.json (used to locate the user config root).</param>
    internal static bool IsLocalModelUsable(string appsettingsPath, string serverConfigPath)
    {
        // Composition root resolves relative model paths against the user config root;
        // File.Exists alone would resolve them against the current working directory
        // and falsely report an installed model as missing.
        var userConfigDir = Path.GetDirectoryName(Path.GetFullPath(appsettingsPath))!;

        bool ExistsResolved(string? p) =>
            !string.IsNullOrEmpty(p) &&
            (File.Exists(p) || File.Exists(Path.Combine(userConfigDir, p)));

        try
        {
            if (File.Exists(appsettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
                if (doc.RootElement.TryGetProperty("llm", out var llm) &&
                    llm.TryGetProperty("model_path", out var mp) &&
                    mp.ValueKind == JsonValueKind.String &&
                    ExistsResolved(mp.GetString()))
                    return true;
            }
        }
        catch (JsonException) { /* malformed handled earlier → not usable */ }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable → not usable */ }

        try
        {
            if (File.Exists(serverConfigPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(serverConfigPath));
                if (doc.RootElement.TryGetProperty("models", out var models))
                    foreach (var m in models.EnumerateArray())
                        if (m.TryGetProperty("path", out var p) &&
                            p.ValueKind == JsonValueKind.String &&
                            ExistsResolved(p.GetString()))
                            return true;
            }
        }
        catch (JsonException) { /* malformed server config → not usable */ }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable → not usable */ }

        return false;
    }
}
