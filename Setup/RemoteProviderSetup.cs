using System.Net.Http;
using System.Net.Http.Headers;
using ECAssistant.Core.Config;
using ECAssistant.Core.Setup;

namespace ECAssistantConsole;

/// <summary>Interactive console prompt flow that configures a remote OpenAI-compatible provider.</summary>
internal static class RemoteProviderSetup
{
    /// <summary>Prompts for endpoint/key/model, verifies reachability, encrypts the key into the key store, saves config.</summary>
    public static async Task RunInteractiveAsync(string appsettingsPath)
    {
        Console.WriteLine();
        Console.WriteLine("── Remote AI setup ──");
        Console.Write("  Endpoint (e.g. https://api.openai.com/v1): ");
        var endpoint = Console.ReadLine()?.Trim() ?? "";
        if (endpoint.Length == 0) { Console.WriteLine("Skipped."); return; }
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            Console.WriteLine("Skipped. (Endpoint is not a valid absolute URL.)");
            return;
        }

        Console.Write("  API key: ");
        var apiKey = Console.ReadLine()?.Trim() ?? "";

        Console.Write("  Model ID (e.g. gpt-4o-mini): ");
        var modelId = Console.ReadLine()?.Trim() ?? "";
        if (modelId.Length == 0) { Console.WriteLine("Skipped."); return; }

        Console.Write("  Embedding model ID [Enter = text-embedding-3-small]: ");
        var embeddingModelId = Console.ReadLine()?.Trim() ?? "";
        if (embeddingModelId.Length == 0) embeddingModelId = "text-embedding-3-small";

        Console.Write("  Does this model support vision (image input)? [y/N]: ");
        var visionEnabled = (Console.ReadLine()?.Trim() ?? "").ToLowerInvariant() is "y" or "yes";

        Console.WriteLine("  Testing connection...");
        if (await IsReachableAsync(endpoint, apiKey))
        {
            Console.WriteLine("  ✔ Endpoint reachable.");
        }
        else
        {
            Console.Write("  Connection failed — save anyway? [y/N]: ");
            var save = (Console.ReadLine()?.Trim() ?? "").ToLowerInvariant();
            if (save != "y" && save != "yes") { Console.WriteLine("Skipped."); return; }
        }

        new RemoteProviderSetupWriter(appsettingsPath).Write(new RemoteProviderConfig
        {
            Name = new UriBuilder(endpoint).Host,
            Endpoint = endpoint,
            ApiKey = apiKey.Length > 0 ? apiKey : null,
            ModelId = modelId,
            EmbeddingModelId = embeddingModelId,
            VisionEnabled = visionEnabled
        });
        Console.WriteLine($"✔ Remote AI configured: {modelId} @ {endpoint} (key encrypted to key store)");
    }

    /// <summary>GET {endpoint}/models with optional bearer auth; true on any HTTP response, false on transport failure.</summary>
    private static async Task<bool> IsReachableAsync(string endpoint, string apiKey)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var resp = await http.GetAsync($"{endpoint.TrimEnd('/')}/models");
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; } // timeout
    }
}
