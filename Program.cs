namespace ECAssistantConsole;

/// <summary>Composition entry point. Dispatches to the test host or the interactive application.</summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // ── Test mode: run automated tests ──
            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
                return await new ConsoleTestHost().RunAsync(args.Skip(1).ToArray());

            // ── Interactive application ──
            return await new ConsoleApplication(args, HostPaths.UserConfigDir).RunAsync();
        }
        catch (OperationCanceledException)
        {
            return 130; // interrupted
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Error] Fatal: {ex.Message}");
            WriteCrashLog(ex);

            // --verbose / -v prints the full exception (stack trace, inner exceptions) to stderr.
            if (args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("-v", StringComparison.OrdinalIgnoreCase)))
                Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    /// <summary>Always records the full exception (including stack trace) to a crash log; best-effort.</summary>
    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(HostPaths.UserConfigDir);
            var logPath = Path.Combine(HostPaths.UserConfigDir, "ecassistant-crash.log");
            File.AppendAllText(logPath,
                $"---- {DateTime.UtcNow:O} ----{Environment.NewLine}{ex}{Environment.NewLine}");
            Console.Error.WriteLine($"[Error] Full details written to: {logPath} (or rerun with --verbose)");
        }
        catch
        {
            // Crash logging must never throw; stderr already carries the message.
        }
    }
}
