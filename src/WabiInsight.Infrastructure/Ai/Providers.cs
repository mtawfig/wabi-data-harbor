using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Ai;

namespace WabiInsight.Infrastructure.Ai;

/// <summary>
/// Shared HTTP plumbing: JSON bodies, uniform error codes, bounded response size. Two named clients: the general
/// one for platform providers, and one for organization-managed providers that only connects to public addresses
/// and never follows a redirect (<see cref="PublicAddressGuard"/>).
/// </summary>
public abstract class HttpAiProvider : IAiProvider
{
    public const string HttpClientName = "WabiInsight.Ai";
    public const string PublicOnlyHttpClientName = "WabiInsight.Ai.PublicOnly";
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    private readonly IHttpClientFactory _factory;

    protected HttpAiProvider(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    public abstract AiProviderType ProviderType { get; }

    public abstract Task<AiCompletion> CompleteAsync(AiProviderDescriptor descriptor, AiRequest request, CancellationToken cancellationToken = default);

    protected async Task<JsonNode> PostAsync(AiProviderDescriptor descriptor, Uri uri, IReadOnlyDictionary<string, string> headers, object body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.PublicAddressesOnly && !PublicAddressGuard.IsAcceptableEndpoint(uri))
        {
            throw new AiProviderException(PublicAddressGuard.ErrorCode, "The AI provider's address is not on the public internet.");
        }

        using var client = _factory.CreateClient(descriptor.PublicAddressesOnly ? PublicOnlyHttpClientName : HttpClientName);
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        foreach (var (name, value) in headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // The connection guard's refusal arrives wrapped by the HTTP client: keep its own code.
            for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            {
                if (inner is AiProviderException refused)
                {
                    throw new AiProviderException(refused.Code, refused.Message);
                }
            }

            throw new AiProviderException("ai_provider_unreachable", ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("ai_provider_timeout", ex.Message);
        }

        using (response)
        {
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                throw new AiProviderException("ai_provider_response_too_large", "Response exceeded the size limit.");
            }

            var text = await ReadBoundedAsync(response.Content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode switch
                {
                    401 or 403 => "ai_provider_unauthorized",
                    404 => "ai_provider_model_not_found",
                    429 => "ai_provider_rate_limited",
                    >= 500 => "ai_provider_unavailable",
                    _ => "ai_provider_rejected_request",
                };
                throw new AiProviderException(code, Truncate(text));
            }

            try
            {
                return JsonNode.Parse(text) ?? throw new AiProviderException("ai_provider_invalid_response", "Empty body.");
            }
            catch (JsonException ex)
            {
                throw new AiProviderException("ai_provider_invalid_response", ex.Message);
            }
        }
    }

    protected static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    /// <summary>Reads the body up to the size limit even when the server sends no Content-Length (chunked).</summary>
    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw new AiProviderException("ai_provider_response_too_large", "Response exceeded the size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    protected static long Count(JsonNode? node) => node is null ? 0 : node.GetValueKind() == JsonValueKind.Number ? node.GetValue<long>() : 0;

    protected static JsonNode ParseSchema(string schema) => JsonNode.Parse(schema) ?? throw new AiProviderException("ai_schema_invalid", "Schema is empty.");
}

/// <summary>Anthropic Messages API (owner's choice: Claude Opus 5.5). Structured output through output_config.format; adaptive thinking with the configured effort.</summary>
public sealed class AnthropicProvider : HttpAiProvider
{
    public const string DefaultEndpoint = "https://api.anthropic.com";
    public const string ApiVersion = "2026-10-21";

    public AnthropicProvider(IHttpClientFactory factory)
        : base(factory)
    {
    }

    public override AiProviderType ProviderType => AiProviderType.Anthropic;

    public override async Task<AiCompletion> CompleteAsync(AiProviderDescriptor descriptor, AiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(request);
        var outputConfig = new Dictionary<string, object?>();
        // A call may ask for its own effort (the interpretation asks for "low"); otherwise the provider's configured effort applies.
        var effort = string.IsNullOrWhiteSpace(request.Effort) ? descriptor.Effort : request.Effort;
        if (!string.IsNullOrWhiteSpace(effort))
        {
            outputConfig["effort"] = effort;
        }

        if (request.JsonSchema is not null)
        {
            outputConfig["format"] = new { type = "json_schema", schema = ParseSchema(request.JsonSchema) };
        }

        // Prompt caching: the system prompt is a cache breakpoint, and so is the shared context (the planner's catalogue), which is
        // the same for consecutive questions on an application. The rest of the user turn (question, date) follows uncached.
        object content = string.IsNullOrEmpty(request.SharedContext)
            ? request.UserContent
            : new object[]
            {
                new { type = "text", text = request.SharedContext, cache_control = new { type = "ephemeral" } },
                new { type = "text", text = request.UserContent },
            };
        var body = new
        {
            model = descriptor.Model,
            max_tokens = request.MaxOutputTokens,
            system = new[] { new { type = "text", text = request.SystemPrompt, cache_control = new { type = "ephemeral" } } },
            messages = new[] { new { role = "user", content } },
            output_config = outputConfig.Count == 0 ? null : outputConfig,
        };
        var headers = new Dictionary<string, string> { ["x-api-key"] = descriptor.ApiKey, ["anthropic-version"] = ApiVersion };
        var json = await PostAsync(descriptor, new Uri($"{(descriptor.Endpoint ?? DefaultEndpoint).TrimEnd('/')}/v1/messages"), headers, body, cancellationToken);

        var stopReason = json["stop_reason"]?.GetValue<string>();
        if (stopReason == "refusal")
        {
            throw new AiProviderException("ai_provider_refused", json["stop_details"]?["explanation"]?.GetValue<string>() ?? "The model declined the request.");
        }

        var text = string.Concat((json["content"] as JsonArray ?? []).Where(b => b?["type"]?.GetValue<string>() == "text").Select(b => b!["text"]?.GetValue<string>() ?? string.Empty));
        var usage = json["usage"];
        return new AiCompletion(text, new AiUsage(Count(usage?["input_tokens"]) + Count(usage?["cache_read_input_tokens"]) + Count(usage?["cache_creation_input_tokens"]), Count(usage?["output_tokens"]), Count(usage?["cache_read_input_tokens"]), Count(usage?["cache_creation_input_tokens"])), json["model"]?.GetValue<string>() ?? descriptor.Model, stopReason);
    }
}

/// <summary>OpenAI-compatible chat completions: OpenAI, Mistral, vLLM, Ollama, LM Studio. JSON schema response format where the server supports it.</summary>
public sealed class OpenAiCompatibleProvider : HttpAiProvider
{
    public const string DefaultEndpoint = "https://api.openai.com/v1";

    /// <summary>OpenAI's service and Azure OpenAI take max_completion_tokens.</summary>
    internal static bool UsesCompletionTokens(Uri endpoint)
        => endpoint.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase) || endpoint.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase);

    public OpenAiCompatibleProvider(IHttpClientFactory factory)
        : base(factory)
    {
    }

    public override AiProviderType ProviderType => AiProviderType.OpenAiCompatible;

    public override async Task<AiCompletion> CompleteAsync(AiProviderDescriptor descriptor, AiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(request);
        var endpoint = new Uri($"{(descriptor.Endpoint ?? DefaultEndpoint).TrimEnd('/')}/chat/completions");
        var body = new Dictionary<string, object?>
        {
            ["model"] = descriptor.Model,
            ["messages"] = new[] { new { role = "system", content = request.SystemPrompt }, new { role = "user", content = request.FullUserContent } },
        };

        // OpenAI's own service takes max_completion_tokens (its reasoning models reject max_tokens); other compatible
        // servers (Mistral, vLLM, Ollama, LM Studio) still expect max_tokens (E7).
        body[UsesCompletionTokens(endpoint) ? "max_completion_tokens" : "max_tokens"] = request.MaxOutputTokens;
        if (request.JsonSchema is not null)
        {
            body["response_format"] = new { type = "json_schema", json_schema = new { name = request.SchemaName, schema = ParseSchema(request.JsonSchema), strict = true } };
        }

        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {descriptor.ApiKey}" };
        var json = await PostAsync(descriptor, endpoint, headers, body, cancellationToken);
        var choice = (json["choices"] as JsonArray)?.FirstOrDefault();
        var message = choice?["message"];
        var text = message?["content"]?.GetValueKind() == JsonValueKind.String ? message["content"]!.GetValue<string>() : string.Empty;
        var finish = choice?["finish_reason"]?.GetValue<string>();
        if (finish == "content_filter")
        {
            throw new AiProviderException("ai_provider_refused", "The provider's content filter declined the request.");
        }

        // With a JSON schema, OpenAI states a refusal in its own field instead of the content.
        if (message?["refusal"]?.GetValueKind() == JsonValueKind.String && text.Length == 0)
        {
            throw new AiProviderException("ai_provider_refused", Truncate(message["refusal"]!.GetValue<string>()));
        }

        var usage = json["usage"];
        return new AiCompletion(text, new AiUsage(Count(usage?["prompt_tokens"]), Count(usage?["completion_tokens"]), Count(usage?["prompt_tokens_details"]?["cached_tokens"])), json["model"]?.GetValue<string>() ?? descriptor.Model, finish);
    }
}

/// <summary>Google Gemini generateContent with a response schema (Gemini's schema dialect: no additionalProperties/$defs, so the schema is simplified).</summary>
public sealed class GeminiProvider : HttpAiProvider
{
    public const string DefaultEndpoint = "https://generativelanguage.googleapis.com";

    public GeminiProvider(IHttpClientFactory factory)
        : base(factory)
    {
    }

    public override AiProviderType ProviderType => AiProviderType.Gemini;

    public override async Task<AiCompletion> CompleteAsync(AiProviderDescriptor descriptor, AiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(request);
        var generation = new Dictionary<string, object?> { ["maxOutputTokens"] = request.MaxOutputTokens };
        if (request.JsonSchema is not null)
        {
            generation["responseMimeType"] = "application/json";
            generation["responseSchema"] = SimplifyForGemini(ParseSchema(request.JsonSchema));
        }

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = request.FullUserContent } } } },
            generationConfig = generation,
        };
        var headers = new Dictionary<string, string> { ["x-goog-api-key"] = descriptor.ApiKey };
        var json = await PostAsync(descriptor, new Uri($"{(descriptor.Endpoint ?? DefaultEndpoint).TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(descriptor.Model)}:generateContent"), headers, body, cancellationToken);
        var candidate = (json["candidates"] as JsonArray)?.FirstOrDefault();
        var finish = candidate?["finishReason"]?.GetValue<string>();
        if (finish is "SAFETY" or "PROHIBITED_CONTENT")
        {
            throw new AiProviderException("ai_provider_refused", "The provider's safety filter declined the request.");
        }

        // Thought summaries (parts marked "thought") are not the answer; thinking tokens are billed as output (E7).
        var text = string.Concat((candidate?["content"]?["parts"] as JsonArray ?? []).Where(p => p?["thought"]?.GetValueKind() != JsonValueKind.True).Select(p => p?["text"]?.GetValue<string>() ?? string.Empty));
        var usage = json["usageMetadata"];
        var model = json["modelVersion"]?.GetValueKind() == JsonValueKind.String ? json["modelVersion"]!.GetValue<string>() : descriptor.Model;
        return new AiCompletion(text, new AiUsage(Count(usage?["promptTokenCount"]), Count(usage?["candidatesTokenCount"]) + Count(usage?["thoughtsTokenCount"]), Count(usage?["cachedContentTokenCount"])), model, finish);
    }

    /// <summary>Inlines $ref definitions and drops keywords Gemini rejects. Validation still happens server-side on our side.</summary>
    internal static JsonNode SimplifyForGemini(JsonNode schema)
    {
        var defs = schema["$defs"] as JsonObject;
        var rewritten = Rewrite(schema, defs, 0);
        Prune(rewritten);
        return rewritten;
    }

    /// <summary>
    /// Gemini rejects an object schema without properties. The recursive filter groups end in one (where the nesting
    /// limit cuts the recursion): such a property is dropped, with its entry in "required", so the deepest level simply
    /// has no further groups. The server's validator enforces the real limits either way.
    /// </summary>
    private static void Prune(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject properties)
                {
                    foreach (var name in properties.Where(p => IsEmptyObject(p.Value)).Select(p => p.Key).ToList())
                    {
                        properties.Remove(name);
                        if (obj["required"] is JsonArray required)
                        {
                            foreach (var item in required.Where(r => r?.GetValue<string>() == name).ToList())
                            {
                                required.Remove(item);
                            }
                        }
                    }
                }

                foreach (var (_, value) in obj.ToList())
                {
                    Prune(value);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Prune(item);
                }

                break;
        }
    }

    private static bool IsEmptyObject(JsonNode? schema) => schema switch
    {
        JsonObject o when o["type"]?.GetValueKind() == JsonValueKind.String && o["type"]!.GetValue<string>() == "array" => IsEmptyObject(o["items"]),
        JsonObject o when o["type"]?.GetValueKind() == JsonValueKind.String && o["type"]!.GetValue<string>() == "object" => o["properties"] is not JsonObject p || p.Count == 0 || p.All(x => IsEmptyObject(x.Value)),
        _ => false,
    };

    /// <summary>How many references deep a recursive definition (a filter group inside a filter group) is expanded.</summary>
    private const int MaxReferenceDepth = 4;

    /// <summary>
    /// Copies the schema, inlining references. <paramref name="depth"/> counts expanded references only, so plain nesting
    /// is copied whole; a reference past the limit becomes an empty object, which <see cref="Prune"/> then removes.
    /// </summary>
    private static JsonNode Rewrite(JsonNode node, JsonObject? defs, int depth)
    {

        switch (node)
        {
            case JsonObject obj:
                if (obj["$ref"] is JsonValue reference && defs is not null)
                {
                    var name = reference.GetValue<string>().Split('/')[^1];
                    return depth < MaxReferenceDepth && defs[name] is { } target ? Rewrite(target, defs, depth + 1) : new JsonObject { ["type"] = "object" };
                }

                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (key is "$defs" or "additionalProperties" or "$schema" or "minItems" or "maxItems" or "minimum" or "maximum")
                    {
                        continue;
                    }

                    if (key == "anyOf" && value is JsonArray options)
                    {
                        // Gemini has no anyOf: take the first non-null option and mark it nullable.
                        var first = options.FirstOrDefault(o => o?["type"]?.GetValue<string>() != "null") ?? options.First();
                        var rewritten = Rewrite(first!, defs, depth);
                        if (rewritten is JsonObject ro)
                        {
                            ro["nullable"] = true;
                            foreach (var (k, v) in ro)
                            {
                                copy[k] = v?.DeepClone();
                            }
                        }

                        continue;
                    }

                    copy[key] = value is null ? null : Rewrite(value, defs, depth);
                }

                return copy;
            case JsonArray array:
                var list = new JsonArray();
                foreach (var item in array)
                {
                    list.Add(item is null ? null : Rewrite(item, defs, depth));
                }

                return list;
            default:
                return node.DeepClone();
        }
    }
}

public sealed class AiProviderFactory : IAiProviderFactory
{
    private readonly Dictionary<AiProviderType, IAiProvider> _providers;

    public AiProviderFactory(IEnumerable<IAiProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.ProviderType);
    }

    public IAiProvider Resolve(AiProviderType providerType)
        => _providers.TryGetValue(providerType, out var provider) ? provider : throw new AiProviderException("ai_provider_type_unsupported", providerType.ToString());
}
