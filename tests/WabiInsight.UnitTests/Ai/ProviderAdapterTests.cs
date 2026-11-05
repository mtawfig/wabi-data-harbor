using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Ai;
using WabiInsight.Infrastructure.Ai;

namespace WabiInsight.UnitTests.Ai;

public sealed class ProviderAdapterTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Response { get; set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(Response, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public Factory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static readonly AiRequest Request = new("SYSTEM", "USER", QueryPlanSchema.Json, QueryPlanSchema.Name, 2048, "medium");

    private static AiProviderDescriptor Descriptor(AiProviderType type, string? endpoint = null) => new(Guid.NewGuid(), type, endpoint, "model-x", "sk-secret-key-1234", 2048, "medium", 4m, 20m, 0.2m);

    [Fact]
    public async Task Anthropic_SendsStructuredOutputAndCachedSystemPrompt_AndReadsUsage()
    {
        var handler = new FakeHandler { Response = """{"model":"claude-opus-5-5","stop_reason":"end_turn","content":[{"type":"text","text":"{\"objective\":\"x\"}"}],"usage":{"input_tokens":1200,"output_tokens":300,"cache_read_input_tokens":1000}}""" };
        var completion = await new AnthropicProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.Anthropic), Request);

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("sk-secret-key-1234", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal(AnthropicProvider.ApiVersion, handler.Request.Headers.GetValues("anthropic-version").Single());
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("json_schema", body["output_config"]!["format"]!["type"]!.GetValue<string>());
        Assert.Equal("medium", body["output_config"]!["effort"]!.GetValue<string>());
        Assert.Equal("ephemeral", body["system"]![0]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Null(body["thinking"]);
        Assert.Equal("{\"objective\":\"x\"}", completion.Text);
        Assert.Equal(2200, completion.Usage.InputTokens);
        Assert.Equal(1000, completion.Usage.CachedInputTokens);
        Assert.Equal(300, completion.Usage.OutputTokens);
        Assert.Equal(0.011m, Descriptor(AiProviderType.Anthropic).EstimateCost(completion.Usage)); // 1200 × $4 + 1000 × $0.20 + 300 × $20 per million
    }

    [Fact]
    public async Task Anthropic_MapsRefusalsAndHttpErrorsToStableCodes()
    {
        var refused = new FakeHandler { Response = """{"stop_reason":"refusal","stop_details":{"type":"refusal","explanation":"no"},"content":[],"usage":{"input_tokens":1,"output_tokens":0}}""" };
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => new AnthropicProvider(new Factory(refused)).CompleteAsync(Descriptor(AiProviderType.Anthropic), Request));
        Assert.Equal("ai_provider_refused", ex.Code);

        var unauthorized = new FakeHandler { Status = HttpStatusCode.Unauthorized, Response = """{"error":{"message":"invalid x-api-key"}}""" };
        ex = await Assert.ThrowsAsync<AiProviderException>(() => new AnthropicProvider(new Factory(unauthorized)).CompleteAsync(Descriptor(AiProviderType.Anthropic), Request));
        Assert.Equal("ai_provider_unauthorized", ex.Code);

        var limited = new FakeHandler { Status = HttpStatusCode.TooManyRequests, Response = "{}" };
        ex = await Assert.ThrowsAsync<AiProviderException>(() => new AnthropicProvider(new Factory(limited)).CompleteAsync(Descriptor(AiProviderType.Anthropic), Request));
        Assert.Equal("ai_provider_rate_limited", ex.Code);
    }

    [Fact]
    public async Task OpenAiCompatible_UsesBearerAndJsonSchemaResponseFormat_OnCustomEndpoint()
    {
        var handler = new FakeHandler { Response = """{"model":"mistral-large","choices":[{"message":{"content":"```json\n{\"a\":1}\n```"},"finish_reason":"stop"}],"usage":{"prompt_tokens":500,"completion_tokens":40,"prompt_tokens_details":{"cached_tokens":100}}}""" };
        var completion = await new OpenAiCompatibleProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible, "http://localhost:11434/v1/"), Request);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer sk-secret-key-1234", handler.Request.Headers.Authorization!.ToString());
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("json_schema", body["response_format"]!["type"]!.GetValue<string>());
        Assert.True(body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal(500, completion.Usage.InputTokens);
        Assert.Equal(100, completion.Usage.CachedInputTokens);
        Assert.Equal("mistral-large", completion.Model);
    }

    [Fact]
    public async Task Gemini_UsesResponseSchemaWithoutUnsupportedKeywords()
    {
        var handler = new FakeHandler { Response = """{"candidates":[{"content":{"parts":[{"text":"{\"a\":1}"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":700,"candidatesTokenCount":50}}""" };
        var completion = await new GeminiProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.Gemini), Request);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/model-x:generateContent", handler.Request!.RequestUri!.ToString());
        Assert.Equal("sk-secret-key-1234", handler.Request.Headers.GetValues("x-goog-api-key").Single());
        var body = handler.Body!;
        Assert.Contains("\"responseMimeType\":\"application/json\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("additionalProperties", body, StringComparison.Ordinal);
        Assert.DoesNotContain("$ref", body, StringComparison.Ordinal);
        Assert.DoesNotContain("$defs", body, StringComparison.Ordinal);
        Assert.Contains("\"nullable\":true", body, StringComparison.Ordinal);
        Assert.Equal(700, completion.Usage.InputTokens);
        Assert.Equal("{\"a\":1}", completion.Text);
    }

    [Fact]
    public void Planner_ParsesFencedAndPaddedJson()
    {
        var plan = QueryPlanner.Parse("Here you go:\n```json\n{\"objective\":\"o\",\"source\":{\"schema\":\"dbo\",\"object\":\"T\",\"alias\":\"t\"},\"joins\":[],\"select\":[],\"filters\":null,\"groupBy\":[],\"orderBy\":[],\"limit\":null,\"visualization\":null,\"assumptions\":[]}\n```");
        Assert.Equal("T", plan.Source.ObjectName);
        Assert.Throws<JsonException>(() => QueryPlanner.Parse("no json here"));
    }
}
