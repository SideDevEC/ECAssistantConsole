using System.Net;
using System.Text;
using ECAssistant.Core.Setup;
using Xunit;

namespace ECAssistantConsole.Tests;

public class RemoteModelProbeTests
{
    private static HttpClient CreateClient(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new FakeHandler(json, status)) { BaseAddress = new Uri("http://localhost") };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public FakeHandler(string json, HttpStatusCode status) { _json = json; _status = status; }
        public string? AuthHeader;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            AuthHeader = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task Probe_OpenAiFormat_ParsesIds()
    {
        using var http = CreateClient("""{"data":[{"id":"gpt-4o"},{"id":"gpt-4o-mini"}]}""");
        var result = await new RemoteModelProbe(http).ProbeAsync("http://x/v1", "k1");
        Assert.True(result.Reachable);
        Assert.Equal(2, result.Models.Count);
        Assert.Equal("gpt-4o", result.Models[0].Id);
    }

    [Fact]
    public async Task Probe_OpenRouterArchitecture_DetectsVision()
    {
        using var http = CreateClient("""
            {"data":[
              {"id":"text-only","architecture":{"input_modalities":["text"]}},
              {"id":"vision-model","architecture":{"input_modalities":["text","image"]}}
            ]}
            """);
        var result = await new RemoteModelProbe(http).ProbeAsync("http://x/v1", null);
        Assert.False(result.Models[0].SupportsVision);
        Assert.True(result.Models[1].SupportsVision);
    }

    [Fact]
    public async Task Probe_SendsBearerAuth_WhenKeyGiven()
    {
        var handler = new FakeHandler("""{"data":[]}""", HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        await new RemoteModelProbe(http).ProbeAsync("http://x/v1", "sekrit");
        Assert.Equal("Bearer sekrit", handler.AuthHeader);
    }

    [Fact]
    public async Task Probe_TransportError_ReturnsUnreachable()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var result = await new RemoteModelProbe(http).ProbeAsync("http://x/v1", null);
        Assert.False(result.Reachable);
        Assert.Empty(result.Models);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("boom");
    }
}
