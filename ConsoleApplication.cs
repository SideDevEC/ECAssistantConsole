using System.Text.Json;
using ECAssistant.Core;
using ECAssistant.Core.Composition;
using ECAssistant.Core.Services;
using ECAssistant.Core.Setup;
using ECAssistant.TUI.Controller;
using ECAssistant.TUI.UI;

namespace ECAssistantConsole;

/// <summary>
/// Thin console host: runs first-run setup, validates model configuration,
/// wires the composition root and hands control to the TUI AppController.
/// Dependency chain (Emre): this project references ONLY ECAssistant.TUI —
/// Core flows transitively and is used directly (nothing hidden or wrapped).
/// </summary>
internal sealed class ConsoleApplication
{
    private readonly string[] _args;
    private readonly string _userConfigDir;

    public ConsoleApplication(string[] args, string userConfigDir)
    {
        _args = args ?? throw new ArgumentNullException(nameof(args));
        _userConfigDir = userConfigDir ?? throw new ArgumentNullException(nameof(userConfigDir));
    }

    public async Task<int> RunAsync()
    {
        var ui = new ConsoleSetupUi();
        var orchestrator = new FirstRunOrchestrator(_userConfigDir, ui);
        await orchestrator.RunIfNeededAsync().ConfigureAwait(false);

        var llmRoot = PathExpander.Default.Expand("~/ECALLM");
        var serverConfigPath = Path.Combine(llmRoot, "llm-server.json");

        if (!IsRemoteModeConfigured() && !FirstRunOrchestrator.IsLocalModelUsable(
                Path.Combine(_userConfigDir, "appsettings.json"),
                serverConfigPath))
        {
            ReportNoLocalModel();
            return 1;
        }

        var root = new EcaCompositionRoot(_userConfigDir, _args);
        EcaServiceBundle services;
        try
        {
            services = root.Build();
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"[Error] Startup failed: {ex.Message}");
            Console.WriteLine($"[Hint] Run again to pick a model from the catalog, or edit model-catalog.json in {_userConfigDir}.");
            return 1;
        }

        if (!File.Exists(services.ModelPath) && !services.Config.LlmProvider.IsRemote)
        {
            Console.WriteLine($"[Error] Model not found: {services.ModelPath}");
            Console.WriteLine($"[Hint] Put your .gguf model in: {_userConfigDir} or set full path in appsettings.json");
            return 1;
        }

        var controller = CreateController(services);
        try
        {
            return await controller.RunAsync();
        }
        finally
        {
            // Init failure (return 1) and exceptions skip the input loop's graceful
            // shutdown — restore the terminal here so the next launch starts clean.
            controller.ShutdownTerminal();
        }
    }

    private AppController CreateController(EcaServiceBundle services)
    {
        var console = new GuiConsole();
        return new AppController(
            console,
            services.Config,
            services.ModelPath,
            services.WorkingDirectory,
            services.UserConfigDirectory,
            services.Logger,
            null, // externalTools: the console host has no plugin loading yet; AppController falls back to an empty tool set.
            services.BackgroundProcesses,
            services.FileWatcher,
            new AiSetupResetter());
    }

    /// <summary>
    /// Reuses FirstRunOrchestrator.IsRemoteProviderConfigured (proper JSON parsing) instead of a
    /// fragile substring match, so both paths agree on what counts as a configured remote provider.
    /// </summary>
    private bool IsRemoteModeConfigured()
    {
        var appsettingsPath = Path.Combine(_userConfigDir, "appsettings.json");
        try
        {
            return File.Exists(appsettingsPath) && FirstRunOrchestrator.IsRemoteProviderConfigured(appsettingsPath);
        }
        // Unreadable/invalid config → treat as not configured; startup falls back to local-model checks.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private void ReportNoLocalModel()
    {
        var llmRoot = PathExpander.Default.Expand("~/ECALLM");
        Console.WriteLine("[Setup] No local model installed yet.");
        Console.WriteLine("[Hint] Run again and pick models from the catalog (or choose remote AI),");
        Console.WriteLine($"       or place a .gguf in {Path.Combine(llmRoot, "models")}.");
    }
}
