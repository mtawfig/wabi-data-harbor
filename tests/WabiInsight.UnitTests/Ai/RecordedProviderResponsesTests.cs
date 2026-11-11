using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Ai;
using WabiInsight.Core.Monitoring;
using WabiInsight.Infrastructure.Ai;
using WabiInsight.Infrastructure.Reports;

namespace WabiInsight.UnitTests.Ai;

/// <summary>
/// P13.29 (E7): the OpenAI-compatible and Gemini adapters against recorded answers. The responses below follow the
/// providers' published response formats (OpenAI Chat Completions with Structured Outputs, Gemini generateContent with
/// thinking); they were written from that reference, not captured from a live account, because no OpenAI or Gemini key
/// is available to this project. What is checked is the request each adapter sends and how it reads each answer.
/// </summary>
public sealed class RecordedProviderResponsesTests
{
    private sealed class Recorded : HttpMessageHandler
    {
        public string Response { get; set; } = "{}";
        public JsonNode? Body { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public Func<HttpContent>? Content { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = Content?.Invoke() ?? new StringContent(Response, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static readonly AiRequest Request = new("SYSTEM", "USER", ReportSchema.Json, ReportSchema.Name, 4096, "medium");

    private static AiProviderDescriptor Descriptor(AiProviderType type, string? endpoint = null, string model = "model-x")
        => new(Guid.NewGuid(), type, endpoint, model, "test-key", 4096, "medium", 2m, 8m, 0.5m);

    // OpenAI Chat Completions, structured output, a reasoning model: reasoning tokens are part of completion_tokens.
    private const string OpenAiAnswer = """
        {"id":"chatcmpl-B9MHDbslfkBeAs8l4bebGdFOJ6PeG","object":"chat.completion","created":1741570283,"model":"gpt-5-2025-08-07",
         "choices":[{"index":0,"message":{"role":"assistant","content":"{\"title\":\"Sales\",\"summary\":\"Sales rose.\"}","refusal":null,"annotations":[]},"logprobs":null,"finish_reason":"stop"}],
         "usage":{"prompt_tokens":1117,"completion_tokens":946,"total_tokens":2063,"prompt_tokens_details":{"cached_tokens":1024,"audio_tokens":0},"completion_tokens_details":{"reasoning_tokens":704,"audio_tokens":0,"accepted_prediction_tokens":0,"rejected_prediction_tokens":0}},
         "service_tier":"default","system_fingerprint":null}
        """;

    private const string OpenAiCutOff = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1741570283,"model":"gpt-5-2025-08-07",
         "choices":[{"index":0,"message":{"role":"assistant","content":"{\"title\":\"Sales\",\"summary\":\"Sales ro","refusal":null},"finish_reason":"length"}],
         "usage":{"prompt_tokens":1117,"completion_tokens":4096,"total_tokens":5213}}
        """;

    private const string OpenAiRefusal = """
        {"id":"chatcmpl-2","object":"chat.completion","created":1741570283,"model":"gpt-5-2025-08-07",
         "choices":[{"index":0,"message":{"role":"assistant","content":null,"refusal":"I'm sorry, I cannot help with that request."},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":50,"completion_tokens":12,"total_tokens":62}}
        """;

    // Gemini generateContent with thinking: a thought-summary part, the answer, and thinking tokens counted separately.
    private const string GeminiAnswer = """
        {"candidates":[{"content":{"parts":[{"text":"**Planning the report** I will summarise the totals first.","thought":true},{"text":"{\"title\":\"Sales\",\"summary\":\"Sales rose.\"}"}],"role":"model"},"finishReason":"STOP","index":0}],
         "usageMetadata":{"promptTokenCount":1210,"candidatesTokenCount":64,"totalTokenCount":2310,"cachedContentTokenCount":1024,"thoughtsTokenCount":1036,"promptTokensDetails":[{"modality":"TEXT","tokenCount":1210}]},
         "modelVersion":"gemini-2.5-pro","responseId":"mx3oaNuWLsCnqtsPqLCO8QY"}
        """;

    private const string GeminiCutOff = """
        {"candidates":[{"content":{"parts":[{"text":"{\"title\":\"Sales\",\"summ"}],"role":"model"},"finishReason":"MAX_TOKENS","index":0}],
         "usageMetadata":{"promptTokenCount":1210,"candidatesTokenCount":10,"totalTokenCount":4106,"thoughtsTokenCount":2886},"modelVersion":"gemini-2.5-flash"}
        """;

    [Fact]
    public async Task OpenAi_SendsMaxCompletionTokensAndAStrictSchema_AndReadsTheAnswerAndItsUsage()
    {
        var handler = new Recorded { Response = OpenAiAnswer };
        var completion = await new OpenAiCompatibleProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible), Request);

        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal(4096, handler.Body!["max_completion_tokens"]!.GetValue<int>());
        Assert.Null(handler.Body["max_tokens"]);
        Assert.Equal("json_schema", handler.Body["response_format"]!["type"]!.GetValue<string>());
        Assert.True(handler.Body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());

        Assert.Equal("{\"title\":\"Sales\",\"summary\":\"Sales rose.\"}", completion.Text);
        Assert.Equal("stop", completion.StopReason);
        Assert.Equal("gpt-5-2025-08-07", completion.Model);
        Assert.Equal(1117, completion.Usage.InputTokens);
        Assert.Equal(946, completion.Usage.OutputTokens);
        Assert.Equal(1024, completion.Usage.CachedInputTokens);
        Assert.Equal("Sales rose.", ReportComposer.Parse(completion.Text, 1, false).Summary);
    }

    [Theory]
    [InlineData("https://api.mistral.ai/v1")]
    [InlineData("http://localhost:11434/v1")]
    public async Task OtherCompatibleServers_StillReceiveMaxTokens(string endpoint)
    {
        var handler = new Recorded { Response = OpenAiAnswer };
        await new OpenAiCompatibleProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible, endpoint), Request);
        Assert.Equal(4096, handler.Body!["max_tokens"]!.GetValue<int>());
        Assert.Null(handler.Body["max_completion_tokens"]);
        Assert.True(OpenAiCompatibleProvider.UsesCompletionTokens(new Uri("https://my-resource.openai.azure.com/openai/v1")));
    }

    [Fact]
    public async Task OpenAi_ACutOffAnswerIsReportedAsCutOff_AndARefusalAsRefused()
    {
        var provider = new OpenAiCompatibleProvider(new Factory(new Recorded { Response = OpenAiCutOff }));
        var cut = await provider.CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible), Request);
        Assert.True(ReportComposer.CutOff(cut.StopReason));
        var content = ReportComposer.Parse(cut.Text, 1, false);
        Assert.True(content.Incomplete);
        Assert.DoesNotContain("{", content.Summary, StringComparison.Ordinal);

        var refused = await Assert.ThrowsAsync<AiProviderException>(() => new OpenAiCompatibleProvider(new Factory(new Recorded { Response = OpenAiRefusal })).CompleteAsync(Descriptor(AiProviderType.OpenAiCompatible), Request));
        Assert.Equal("ai_provider_refused", refused.Code);
    }

    [Fact]
    public async Task Gemini_LeavesThoughtsOut_CountsThinkingAsOutput_AndNamesTheServedModel()
    {
        var handler = new Recorded { Response = GeminiAnswer };
        var completion = await new GeminiProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.Gemini, model: "gemini-2.5-pro"), Request);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent", handler.Request!.RequestUri!.ToString());
        Assert.Equal("{\"title\":\"Sales\",\"summary\":\"Sales rose.\"}", completion.Text);
        Assert.Equal(64 + 1036, completion.Usage.OutputTokens);
        Assert.Equal(1210, completion.Usage.InputTokens);
        Assert.Equal(1024, completion.Usage.CachedInputTokens);
        Assert.Equal("gemini-2.5-pro", completion.Model);

        var cut = await new GeminiProvider(new Factory(new Recorded { Response = GeminiCutOff })).CompleteAsync(Descriptor(AiProviderType.Gemini, model: "gemini-2.5-flash"), Request);
        Assert.True(ReportComposer.CutOff(cut.StopReason));
        Assert.True(ReportComposer.Parse(cut.Text, 1, false).Incomplete);
    }

    public static TheoryData<string, string> Schemas() => new()
    {
        { "plan", AnalysisPlanSchema.Json },
        { "single query", QueryPlanSchema.Json },
        { "report", ReportSchema.Json },
        { "interpretation", AnalysisService.InterpretationSchema },
        { "rule draft", RuleDraftSchema.Json },
    };

    [Theory]
    [MemberData(nameof(Schemas))]
    public void Gemini_SchemasHaveNoObjectWithoutProperties_AndNoUnsupportedKeywords(string name, string schema)
    {
        var simplified = GeminiProvider.SimplifyForGemini(JsonNode.Parse(schema)!);
        var problems = new List<string>();
        Walk(simplified, "$", problems);
        Assert.True(problems.Count == 0, $"{name}: {string.Join("; ", problems)}");
    }

    private static void Walk(JsonNode? node, string path, List<string> problems)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var keyword in new[] { "$ref", "$defs", "anyOf", "oneOf", "allOf", "additionalProperties", "$schema" })
                {
                    if (obj.ContainsKey(keyword))
                    {
                        problems.Add($"{path} has {keyword}");
                    }
                }

                if (obj["type"] is JsonValue type && type.GetValueKind() == JsonValueKind.String && type.GetValue<string>() == "object"
                    && (obj["properties"] is not JsonObject properties || properties.Count == 0))
                {
                    problems.Add($"{path} is an object without properties");
                }

                if (obj["required"] is JsonArray required && obj["properties"] is JsonObject props)
                {
                    problems.AddRange(required.Select(r => r!.GetValue<string>()).Where(r => !props.ContainsKey(r)).Select(r => $"{path} requires missing '{r}'"));
                }

                foreach (var (key, value) in obj)
                {
                    Walk(value, $"{path}.{key}", problems);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    Walk(array[i], $"{path}[{i}]", problems);
                }

                break;
        }
    }

    [Fact]
    public async Task AnAnswerLargerThanTheLimit_IsRefusedEvenWithoutAContentLength()
    {
        var handler = new Recorded { Content = () => new StreamContent(new EndlessStream()) };
        Assert.Null(handler.Content!().Headers.ContentLength);
        var tooLarge = await Assert.ThrowsAsync<AiProviderException>(() => new GeminiProvider(new Factory(handler)).CompleteAsync(Descriptor(AiProviderType.Gemini), Request));
        Assert.Equal("ai_provider_response_too_large", tooLarge.Code);
    }

    /// <summary>A chunked body that never ends and whose length is unknown.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)' ', offset, count);
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
