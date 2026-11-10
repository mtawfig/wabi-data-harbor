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

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("203.0.113.9", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.254", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]      // cloud metadata service
    [InlineData("100.64.0.1", false)]           // shared address space
    [InlineData("100.128.0.1", true)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]      // IPv4 written as IPv6
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::a00:1", false)]       // NAT64 form of 10.0.0.1
    public void PublicAddressGuard_TellsPublicAddressesFromInternalOnes(string address, bool expected)
    {
        Assert.Equal(expected, PublicAddressGuard.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("https://api.mistral.ai/v1", true)]
    [InlineData("https://203.0.113.9/v1", true)]
    [InlineData("http://api.mistral.ai/v1", false)]         // not https
    [InlineData("http://localhost:8000/v1", false)]
    [InlineData("https://localhost/v1", false)]
    [InlineData("https://127.0.0.1/v1", false)]
    [InlineData("https://[::1]/v1", false)]
    [InlineData("https://10.0.0.5/v1", false)]
    [InlineData("https://169.254.169.254/latest/meta-data", false)]
    [InlineData("https://intranet/v1", false)]              // a single-label name only resolves inside a network
    [InlineData("https://models.internal/v1", false)]
    [InlineData("https://printer.local/v1", false)]
    [InlineData("https://user:pass@api.mistral.ai/v1", false)]
    public void PublicAddressGuard_JudgesTheSavedAddress(string endpoint, bool expected)
    {
        Assert.Equal(expected, PublicAddressGuard.IsAcceptableEndpoint(new Uri(endpoint)));
    }

    [Fact]
    public async Task OrganizationProvider_IsNeverCalledAtAnInternalAddress_AndUsesTheGuardedClient()
    {
        var handler = new FakeHandler { Response = """{"model":"m","choices":[{"message":{"content":"OK"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""" };
        var names = new List<string>();
        var provider = new OpenAiCompatibleProvider(new NamingFactory(handler, names));

        // A row saved before the rule existed (or edited in the database) is refused when it is used, before any request.
        var inside = Descriptor(AiProviderType.OpenAiCompatible, "http://localhost:8000/v1") with { PublicAddressesOnly = true };
        var refused = await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(inside, Request));
        Assert.Equal(PublicAddressGuard.ErrorCode, refused.Code);
        Assert.Null(handler.Request);

        // A public address goes out through the client whose connections are checked; a platform provider through the general one.
        await provider.CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible, "https://api.mistral.ai/v1") with { PublicAddressesOnly = true }, Request);
        await provider.CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible, "http://localhost:8000/v1"), Request);
        Assert.Equal([HttpAiProvider.PublicOnlyHttpClientName, HttpAiProvider.HttpClientName], names);
    }

    [Fact]
    public async Task GuardedConnection_RefusesAnInternalAddress_AndTheRefusalKeepsItsCode()
    {
        // The connection callback itself: the name resolves to this machine, so no connection is opened.
        using var guarded = new SocketsHttpHandler { ConnectCallback = PublicAddressGuard.ConnectAsync, AllowAutoRedirect = false };
        using var client = new HttpClient(guarded) { Timeout = TimeSpan.FromSeconds(20) };
        var blocked = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("https://localhost:9/")));
        Assert.Equal(PublicAddressGuard.ErrorCode, Refusal(blocked)?.Code);

        // Through a provider, the wrapped refusal is reported with its own code, not as "unreachable".
        var provider = new OpenAiCompatibleProvider(new Factory(new ThrowingHandler(blocked)));
        var refused = await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible, "https://api.mistral.ai/v1") with { PublicAddressesOnly = true }, Request));
        Assert.Equal(PublicAddressGuard.ErrorCode, refused.Code);
    }

    private static AiProviderException? Refusal(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is AiProviderException refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(_exception);
    }

    private sealed class NamingFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly List<string> _names;

        public NamingFactory(HttpMessageHandler handler, List<string> names)
        {
            _handler = handler;
            _names = names;
        }

        public HttpClient CreateClient(string name)
        {
            _names.Add(name);
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

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
        Assert.Equal("T", Assert.Single(plan.Queries).Source.ObjectName);
        Assert.Equal("o", plan.Objective);
        var dashboard = QueryPlanner.Parse("""{"objective":"d","assumptions":["a"],"queries":[{"title":"Flights","source":{"schema":"dbo","object":"T","alias":"t"},"joins":[],"select":[],"filters":null,"groupBy":[],"orderBy":[],"limit":null,"visualization":{"type":"kpi","x":null,"y":[]}},{"title":"By month","source":{"schema":"dbo","object":"T","alias":"t"},"joins":[],"select":[],"filters":null,"groupBy":[],"orderBy":[],"limit":null,"visualization":null}]}""");
        Assert.Equal(2, dashboard.Queries.Count);
        Assert.Equal("Flights", dashboard.Queries[0].Title);
        Assert.Equal("Flights", dashboard.Queries[0].Objective);
        Assert.Throws<JsonException>(() => QueryPlanner.Parse("no json here"));
    }

    [Fact]
    public async Task Anthropic_CachesTheSharedContext_InItsOwnBlockBeforeTheQuestion_AndPricesCacheWrites()
    {
        var handler = new FakeHandler { Response = """{"model":"m","stop_reason":"end_turn","content":[{"type":"text","text":"{}"}],"usage":{"input_tokens":200,"output_tokens":100,"cache_read_input_tokens":0,"cache_creation_input_tokens":3000}}""" };
        var completion = await new AnthropicProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.Anthropic), Request with { SharedContext = "CATALOGUE\n" });

        var content = JsonNode.Parse(handler.Body!)!["messages"]![0]!["content"]!.AsArray();
        Assert.Equal(2, content.Count);
        Assert.Equal("CATALOGUE\n", content[0]!["text"]!.GetValue<string>());
        Assert.Equal("ephemeral", content[0]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Equal("USER", content[1]!["text"]!.GetValue<string>());
        Assert.Null(content[1]!["cache_control"]);
        Assert.Equal(3200, completion.Usage.InputTokens);
        Assert.Equal(3000, completion.Usage.CacheWriteTokens);
        // 200 × $4 + 3000 × $4 × 1.25 (cache write) + 100 × $20 per million
        Assert.Equal(0.0178m, Descriptor(AiProviderType.Anthropic).EstimateCost(completion.Usage));

        // Without shared context the user turn stays one plain string, as before.
        await new AnthropicProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.Anthropic), Request);
        Assert.Equal("USER", JsonNode.Parse(handler.Body!)!["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task OtherProviders_ReceiveTheSharedContextJoinedToTheQuestion()
    {
        var shared = Request with { SharedContext = "CATALOGUE\n" };
        var openAi = new FakeHandler { Response = """{"choices":[{"message":{"content":"{}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""" };
        await new OpenAiCompatibleProvider(new Factory(openAi)).CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible), shared);
        Assert.Equal("CATALOGUE\nUSER", JsonNode.Parse(openAi.Body!)!["messages"]![1]!["content"]!.GetValue<string>());

        var gemini = new FakeHandler { Response = """{"candidates":[{"content":{"parts":[{"text":"{}"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":1,"candidatesTokenCount":1}}""" };
        await new GeminiProvider(new Factory(gemini)).CompleteAsync(Descriptor(AiProviderType.Gemini), shared);
        Assert.Equal("CATALOGUE\nUSER", JsonNode.Parse(gemini.Body!)!["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Anthropic_UsesTheRequestsOwnEffort_WhenItAsksForOne()
    {
        // The interpretation asks for "low" effort; it used to inherit the provider's configured effort and spend its whole
        // output budget reasoning, leaving no text (a blank explanation).
        var handler = new FakeHandler { Response = """{"model":"m","stop_reason":"end_turn","content":[{"type":"text","text":"{}"}],"usage":{"input_tokens":1,"output_tokens":1}}""" };
        await new AnthropicProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.Anthropic), Request with { Effort = "low" });
        Assert.Equal("low", JsonNode.Parse(handler.Body!)!["output_config"]!["effort"]!.GetValue<string>());

        await new AnthropicProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.Anthropic), Request with { Effort = null });
        Assert.Equal("medium", JsonNode.Parse(handler.Body!)!["output_config"]!["effort"]!.GetValue<string>());
    }

    [Fact]
    public void Interpretation_CutOffOrEmpty_IsMarkedIncomplete_AndNeverShownAsAJsonFragment()
    {
        var usage = new PlanUsage("Anthropic", "m", 1, 1, 0, 0m, 1);
        var complete = AnalysisService.ParseInterpretation("""{"narrative":"Two accounts stand out.","highlights":["A","B"]}""", cutOff: false, usage);
        Assert.False(complete.Incomplete);
        Assert.Equal("Two accounts stand out.", complete.Narrative);
        Assert.Equal(["A", "B"], complete.Highlights);

        // The provider stopped mid-JSON: no fragment such as {"narrative":"Two acc reaches the page.
        var cut = AnalysisService.ParseInterpretation("""{"narrative":"Two acc""", cutOff: true, usage);
        Assert.True(cut.Incomplete);
        Assert.Equal(string.Empty, cut.Narrative);

        // The whole budget went to reasoning: no text at all.
        Assert.True(AnalysisService.ParseInterpretation(string.Empty, cutOff: true, usage).Incomplete);
        Assert.True(AnalysisService.ParseInterpretation("""{"narrative":"","highlights":[]}""", cutOff: false, usage).Incomplete);

        // A complete reply that is plain text is still shown as text.
        var plain = AnalysisService.ParseInterpretation("Most tickets are open.", cutOff: false, usage);
        Assert.False(plain.Incomplete);
        Assert.Equal("Most tickets are open.", plain.Narrative);
        Assert.True(AnalysisService.MinInterpretOutputTokens >= 4096);
    }
}
