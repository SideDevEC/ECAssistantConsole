using ECAssistant.Core;
using ECAssistant.Core.Composition;
using ECAssistant.Core.Services;
using ECAssistant.TUI.Controller;
using ECAssistant.TUI.UI;

namespace ECAssistantConsole;

/// <summary>
/// Thin console host: runs first-run setup, validates model configuration,
/// wires the composition root and hands control to the TUI AppController.
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
        await new FirstRunSetup(_userConfigDir).RunIfNeededAsync();

        if (!IsRemoteModeConfigured() && !FirstRunSetup.IsLocalModelUsable(
                Path.Combine(_userConfigDir, "appsettings.json"),
                Path.Combine(_userConfigDir, "llm", "llm-server.json")))
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
        return await controller.RunAsync();
    }

    private AppController CreateController(EcaServiceBundle services)
    {
        var console = new EGuiConsole();
        return new AppController(
            console,
            services.Config,
            services.ModelPath,
            services.WorkingDirectory,
            services.UserConfigDirectory,
            services.Logger,
            null,
            services.BackgroundProcesses,
            services.FileWatcher);
    }

    private bool IsRemoteModeConfigured()
    {
        var appsettingsPath = Path.Combine(_userConfigDir, "appsettings.json");
        if (!File.Exists(appsettingsPath)) return false;
        return File.ReadAllText(appsettingsPath).Contains("\"mode\": \"remote\"");
    }

    private void ReportNoLocalModel()
    {
        Console.WriteLine("[Setup] No local model installed yet.");
        Console.WriteLine("[Hint] Run again and pick models from the catalog (or choose remote AI),");
        Console.WriteLine($"       or place a .gguf in {Path.Combine(_userConfigDir, "llm", "models")}.");
    }
}
