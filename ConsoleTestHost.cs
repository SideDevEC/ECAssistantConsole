using ECAssistant.Core;
using ECAssistant.Core.Config;
using ECAssistant.Core.Testing;

namespace ECAssistantConsole;

/// <summary>
/// Entry point for `ecassistant --test [...]`: builds a config, applies CLI overrides
/// and runs the Core EcaTestSuite (optionally filtered/verbose, or with the mock engine).
/// </summary>
internal sealed class ConsoleTestHost
{
    private static readonly string UserConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ECAssistant");

    public async Task<int> RunAsync(string[] testArgs)
    {
        var useMock = testArgs.Any(a => a.Equals("--mock", StringComparison.OrdinalIgnoreCase));

        var config = AgentConfigBuilder.Create()
            .WorkingDirectory(UserConfigDir)
            .Build();
        var modelPath = ResolveModelPath(config.Llm.ModelPath, testArgs);

        if (!useMock && (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath)))
        {
            Console.WriteLine($"❌ Model not found: {modelPath}");
            Console.WriteLine("   Set model path in ~/ECAssistant/appsettings.json or pass --model /path/to/model.gguf");
            Console.WriteLine("   Or use --mock for model-independent tests (no GGUF needed).");
            return 1;
        }

        var (filter, verbose) = ParseOptions(testArgs);
        var tests = SelectTests(useMock, filter);
        if (tests is null) return 1; // no match — options already printed

        Console.WriteLine($"\n🧪 Running {tests.Count} test(s) with {(useMock ? "MOCK ENGINE (no model)" : $"model: {Path.GetFileName(modelPath)}")}");
        Console.WriteLine($"   Filter: {filter ?? "(all)"}\n");

        var (results, resultsLogDir) = await ExecuteAsync(tests, useMock, modelPath, verbose);
        var logPath = WriteResultsLog(results, useMock, modelPath, resultsLogDir);
        Console.WriteLine($"\n📝 Detailed log: {logPath}");

        return results.Any(r => !r.Passed) ? 1 : 0;
    }

    private static string ResolveModelPath(string defaultModelPath, string[] testArgs)
    {
        for (var i = 0; i < testArgs.Length; i++)
            if (testArgs[i].Equals("--model", StringComparison.OrdinalIgnoreCase) && i + 1 < testArgs.Length)
                return testArgs[++i];
        return defaultModelPath;
    }

    private static (string? Filter, bool Verbose) ParseOptions(string[] testArgs)
    {
        string? filter = null;
        var verbose = false;
        for (var i = 0; i < testArgs.Length; i++)
        {
            if (testArgs[i].Equals("--filter", StringComparison.OrdinalIgnoreCase) && i + 1 < testArgs.Length)
                filter = testArgs[++i];
            if (testArgs[i].Equals("--verbose", StringComparison.OrdinalIgnoreCase) || testArgs[i].Equals("-v", StringComparison.OrdinalIgnoreCase))
                verbose = true;
        }
        return (filter, verbose);
    }

    private static IReadOnlyList<TestScenario>? SelectTests(bool useMock, string? filter)
    {
        var suite = new EcaTestSuite();
        var allTests = useMock ? suite.MockScenarios : suite.All;

        if (string.IsNullOrEmpty(filter)) return allTests;

        var matches = allTests
            .Where(t => t.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count > 0) return matches;

        Console.WriteLine($"No tests match filter '{filter}'. Available:");
        foreach (var t in allTests)
            Console.WriteLine($"  {t.Name}");
        return null;
    }

    private static async Task<(IReadOnlyList<TestResult> Results, string TestRootDir)> ExecuteAsync(
        IReadOnlyList<TestScenario> tests, bool useMock, string modelPath, bool verbose)
    {
        IReadOnlyList<TestResult> results = Array.Empty<TestResult>();
        string testRootDir = "";
        await using (var runner = new TestRunner(useMock ? "/mock/model.gguf" : modelPath)
        {
            Verbose = verbose,
            UseMockEngine = useMock
        })
        {
            testRootDir = runner.TestRootDir;
            results = await runner.RunAllAsync(tests);
        }
        return (results, testRootDir);
    }

    private static string WriteResultsLog(
        IReadOnlyList<TestResult> results, bool useMock, string modelPath, string testRootDir)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ECAssistant Test Results — {DateTime.UtcNow:O}");
        sb.AppendLine($"Model: {(useMock ? "MOCK ENGINE" : modelPath)}");
        sb.AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine($"{(r.Passed ? "PASS" : "FAIL")} | {r.Name} | {r.Duration.TotalSeconds:F1}s | {r.FailureReason}");
            if (!r.Passed)
            {
                sb.AppendLine($"  Output: {r.FinalOutput}");
                sb.AppendLine();
            }
        }

        var logPath = Path.Combine(testRootDir, "test_results.log");
        File.WriteAllText(logPath, sb.ToString());
        return logPath;
    }
}
