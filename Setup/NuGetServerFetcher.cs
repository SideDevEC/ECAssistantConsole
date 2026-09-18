using System.IO.Compression;
using ECAssistant.Core.Setup;

namespace ECAssistantConsole;

/// <summary>
/// Downloads the ECAssistant.LLM.Server NuGet package from nuget.org and installs
/// the server runtime to the shared standalone location (~/.ECAssistantLLM/server/).
///
/// The console tool package is intentionally thin: the LLM server is NOT embedded.
/// Instead, at first-run/wizard time the matching server package version is fetched
/// from nuget.org, extracted to a temporary directory, and handed to the existing
/// ServerBinaryInstaller for the actual install (skips logs, stamps versions).
///
/// Download happens ONCE during setup — never during chat (boundary rule).
/// </summary>
internal sealed class NuGetServerFetcher
{
    private const string NuGetFlatContainerBase = "https://api.nuget.org/v3-flatcontainer/";
    private const string PackageIdLower = "ecassistant.llm.server";

    private readonly HttpClient _http;
    private readonly string _version;
    private readonly string _targetServerDir;
    private readonly string _tempRoot;

    /// <param name="http">HttpClient owned by the caller (wizard scope).</param>
    /// <param name="version">ECAssistant.LLM.Server package version to fetch
    /// (e.g. "14.7.8" — must match the version the console was built against).</param>
    /// <param name="targetServerDir">Shared server directory, typically ~/.ECAssistantLLM/server/.</param>
    /// <param name="tempRoot">Root for the temporary download/extract directory.</param>
    public NuGetServerFetcher(HttpClient http, string version, string targetServerDir, string? tempRoot = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Version must not be empty.", nameof(version))
            : version;
        _targetServerDir = targetServerDir ?? throw new ArgumentNullException(nameof(targetServerDir));
        _tempRoot = tempRoot ?? Path.GetTempPath();
    }

    /// <summary>
    /// Downloads and installs the server runtime. Returns true on success.
    /// Idempotent: skips when the target already has the binary.
    /// </summary>
    public async Task<bool> FetchAndInstallAsync(CancellationToken cancellationToken = default)
    {
        var existing = new ServerBinaryInstaller(sourceServerDir: _targetServerDir, targetServerDir: _targetServerDir);
        if (existing.IsInstalled())
        {
            Console.WriteLine($"[Setup] LLM server already installed at {_targetServerDir} — skipping download.");
            return true;
        }

        var url = $"{NuGetFlatContainerBase}{PackageIdLower}/{_version}/{PackageIdLower}.{_version}.nupkg";

        var tempDir = Path.Combine(_tempRoot, $"ecassistant-server-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nupkgPath = Path.Combine(tempDir, $"{PackageIdLower}.{_version}.nupkg");
            await DownloadAsync(url, nupkgPath, cancellationToken).ConfigureAwait(false);

            var extractDir = Path.Combine(tempDir, "pkg");
            ZipFile.ExtractToDirectory(nupkgPath, extractDir);

            // Server runtime lives under content/server/ inside the nupkg.
            var contentServerDir = Path.Combine(extractDir, "content", "server");
            if (!Directory.Exists(contentServerDir))
                throw new InvalidOperationException(
                    $"Unexpected package layout: '{contentServerDir}' not found in ECAssistant.LLM.Server {_version}.");

            var installer = new ServerBinaryInstaller(contentServerDir, _targetServerDir);
            installer.Install();
            Console.WriteLine($"[Setup] LLM server {_version} installed to {_targetServerDir}");
            return true;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Setup] Downloading LLM server {_version} from nuget.org (~170 MB)…");

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(destinationPath);

        var buffer = new byte[1 << 16];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            if (total.HasValue && written % (10L << 20) < buffer.Length)
                Console.WriteLine($"[Setup]   {written / (1024 * 1024)} / {total.Value / (1024 * 1024)} MB");
        }

        Console.WriteLine($"[Setup] Download complete ({written / (1024 * 1024)} MB).");
    }
}
