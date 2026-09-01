namespace ECAssistantConsole;

/// <summary>
/// Central definition of host-wide paths so Program.cs, ConsoleTestHost.cs and
/// the composition root all agree on the per-user configuration directory.
/// </summary>
internal static class HostPaths
{
    private static readonly string? UserConfigDirField = ResolveUserConfigDir();

    /// <summary>Per-user config directory (~/ECAssistant). Single source of truth for the console host.</summary>
    public static string UserConfigDir => UserConfigDirField!;

    private static string ResolveUserConfigDir()
    {
        // Some hosts (Windows services, certain CI/sandbox environments) return an
        // empty string for UserProfile; fall back to the app base directory so the
        // host still has a writable root instead of producing a path like "/ECAssistant".
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile)
            ? AppContext.BaseDirectory
            : Path.Combine(profile, "ECAssistant");
    }
}
