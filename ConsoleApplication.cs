using ECAssistant.TUI.Controller;
using ECAssistant.TUI.Hosting;

namespace ECAssistantConsole;

/// <summary>
/// Thin console host: runs first-run setup, validates model configuration,
/// builds the app through the TUI host facade and hands control to the AppController.
/// Depends on the TUI ONLY — the dependency chain is strictly Console → TUI → Core.
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
        await TuiAppHost.RunFirstRunSetupAsync(_userConfigDir).ConfigureAwait(false);

        if (!TuiAppHost.IsRemoteModeConfigured(_userConfigDir) && !TuiAppHost.IsLocalModelUsable(_userConfigDir))
        {
            ReportNoLocalModel();
            return 1;
        }

        AppController controller;
        try
        {
            controller = TuiAppHost.CreateApp(_userConfigDir, _args);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"[Error] Startup failed: {ex.Message}");
            Console.WriteLine($"[Hint] Run again to pick a model from the catalog, or edit model-catalog.json in {_userConfigDir}.");
            return 1;
        }

        return await controller.RunAsync();
    }

    private void ReportNoLocalModel()
    {
        Console.WriteLine("[Setup] No local model installed yet.");
        Console.WriteLine("[Hint] Run again and pick models from the catalog (or choose remote AI),");
        Console.WriteLine($"       or place a .gguf in {Path.Combine(TuiAppHost.LlmRoot(), "models")}.");
    }
}
