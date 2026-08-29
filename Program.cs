namespace ECAssistantConsole;

/// <summary>Composition entry point. Dispatches to the test host or the interactive application.</summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var userConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ECAssistant");

        try
        {
            // ── Test mode: run automated tests ──
            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
                return await new ConsoleTestHost().RunAsync(args.Skip(1).ToArray());

            // ── Interactive application ──
            return await new ConsoleApplication(args, userConfigDir).RunAsync();
        }
        catch (OperationCanceledException)
        {
            return 130; // interrupted
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Error] Fatal: {ex.Message}");
            return 1;
        }
    }
}
