using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace STS2AIAgent.Agent;

internal sealed class AiOpenAiCompatibleClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public async Task<AiDecisionResult> RequestDecisionAsync(AiAgentConfig config, AiPrompt prompt, CancellationToken cancellationToken)
    {
        return await RequestDecisionAsync(
            config,
            prompt,
            new AiLlmCallContext(),
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiDecisionResult> RequestDecisionAsync(
        AiAgentConfig config,
        AiPrompt prompt,
        AiLlmCallContext callContext,
        Action<string>? debugLogFailureSink,
        CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new ChatMessage("system", prompt.SystemPrompt),
            new ChatMessage("user", prompt.UserPrompt)
        };
        var sanitized = ValidateAndSanitizeConfig(config);
        var requestPayload = BuildRequestPayload(sanitized, messages);
        var requestJson = JsonSerializer.Serialize(requestPayload, JsonOptions);
        var requestId = Guid.NewGuid().ToString("N");
        var startedUtc = DateTime.UtcNow;
        ChatCompletionTrace? responseTrace = null;

        try
        {
            responseTrace = await RequestChatCompletionAsync(sanitized, requestJson, cancellationToken).ConfigureAwait(false);
            EnsureSuccessResponse(responseTrace.Value);
            var content = ExtractMessageContent(responseTrace.Value.ResponseBody);
            var decision = ParseDecisionFromContent(content);

            WriteLlmExchangeWithDiagnostics(config, new
            {
                timestamp_utc = startedUtc,
                finished_utc = DateTime.UtcNow,
                request_id = requestId,
                type = "llm_call",
                success = true,
                context = callContext,
                config = new
                {
                    provider = sanitized.provider,
                    base_url = sanitized.base_url,
                    model = sanitized.model,
                    temperature = sanitized.temperature
                },
                request = new
                {
                    uri = BuildChatCompletionsUri(sanitized.base_url).ToString(),
                    json = requestJson,
                    messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray()
                },
                response = new
                {
                    status_code = responseTrace.Value.StatusCode,
                    body = responseTrace.Value.ResponseBody,
                    content
                },
                parsed = decision
            }, debugLogFailureSink);

            return decision;
        }
        catch (Exception ex)
        {
            WriteLlmExchangeWithDiagnostics(config, new
            {
                timestamp_utc = startedUtc,
                finished_utc = DateTime.UtcNow,
                request_id = requestId,
                type = "llm_call",
                success = false,
                context = callContext,
                config = new
                {
                    provider = sanitized.provider,
                    base_url = sanitized.base_url,
                    model = sanitized.model,
                    temperature = sanitized.temperature
                },
                request = new
                {
                    uri = BuildChatCompletionsUri(sanitized.base_url).ToString(),
                    json = requestJson,
                    messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray()
                },
                response = responseTrace == null
                    ? null
                    : new
                    {
                        status_code = responseTrace.Value.StatusCode,
                        body = responseTrace.Value.ResponseBody
                    },
                error = ex.Message
            }, debugLogFailureSink);

            throw;
        }
    }

    public async Task<string> TestConnectionAsync(AiAgentConfig config, CancellationToken cancellationToken)
    {
        return await TestConnectionAsync(config, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> TestConnectionAsync(
        AiAgentConfig config,
        Action<string>? debugLogFailureSink,
        CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new ChatMessage("system", "You are a connectivity probe. Reply with a short plain text acknowledgement."),
            new ChatMessage("user", "Respond with: OK")
        };
        var sanitized = ValidateAndSanitizeConfig(config);
        var requestPayload = BuildRequestPayload(sanitized, messages);
        var requestJson = JsonSerializer.Serialize(requestPayload, JsonOptions);
        var requestId = Guid.NewGuid().ToString("N");
        var startedUtc = DateTime.UtcNow;
        ChatCompletionTrace? responseTrace = null;

        try
        {
            responseTrace = await RequestChatCompletionAsync(sanitized, requestJson, cancellationToken).ConfigureAwait(false);
            EnsureSuccessResponse(responseTrace.Value);
            var content = ExtractMessageContent(responseTrace.Value.ResponseBody);

            WriteLlmExchangeWithDiagnostics(config, new
            {
                timestamp_utc = startedUtc,
                finished_utc = DateTime.UtcNow,
                request_id = requestId,
                type = "llm_connectivity_probe",
                success = true,
                context = new AiLlmCallContext { purpose = "test_connection" },
                config = new
                {
                    provider = sanitized.provider,
                    base_url = sanitized.base_url,
                    model = sanitized.model,
                    temperature = sanitized.temperature
                },
                request = new
                {
                    uri = BuildChatCompletionsUri(sanitized.base_url).ToString(),
                    json = requestJson,
                    messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray()
                },
                response = new
                {
                    status_code = responseTrace.Value.StatusCode,
                    body = responseTrace.Value.ResponseBody,
                    content
                }
            }, debugLogFailureSink);

            return TrimForError(content);
        }
        catch (Exception ex)
        {
            WriteLlmExchangeWithDiagnostics(config, new
            {
                timestamp_utc = startedUtc,
                finished_utc = DateTime.UtcNow,
                request_id = requestId,
                type = "llm_connectivity_probe",
                success = false,
                context = new AiLlmCallContext { purpose = "test_connection" },
                config = new
                {
                    provider = sanitized.provider,
                    base_url = sanitized.base_url,
                    model = sanitized.model,
                    temperature = sanitized.temperature
                },
                request = new
                {
                    uri = BuildChatCompletionsUri(sanitized.base_url).ToString(),
                    json = requestJson,
                    messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray()
                },
                response = responseTrace == null
                    ? null
                    : new
                    {
                        status_code = responseTrace.Value.StatusCode,
                        body = responseTrace.Value.ResponseBody
                    },
                error = ex.Message
            }, debugLogFailureSink);

            throw;
        }
    }

    private async Task<ChatCompletionTrace> RequestChatCompletionAsync(AiAgentConfig sanitized, string requestJson, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri(sanitized.base_url));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sanitized.api_key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new ChatCompletionTrace((int)response.StatusCode, body);
    }

    private static AiDecisionResult ParseDecisionFromContent(string content)
    {
        var jsonPayload = ExtractJsonPayload(content);
        using var decisionDocument = JsonDocument.Parse(jsonPayload);
        var root = decisionDocument.RootElement;

        var actionElement = root.TryGetProperty("action", out var parsedAction)
            ? parsedAction
            : default;

        return new AiDecisionResult
        {
            plan_summary = ReadString(root, "plan_summary"),
            reasoning = ReadString(root, "reasoning"),
            action = new AiActionSuggestion
            {
                name = ReadString(actionElement, "name", "none"),
                card_index = ReadNullableInt(actionElement, "card_index"),
                target_index = ReadNullableInt(actionElement, "target_index"),
                option_index = ReadNullableInt(actionElement, "option_index")
            },
            requires_confirmation = ReadBool(root, "requires_confirmation", true),
            stop_reason = ReadString(root, "stop_reason"),
            safety_checks = ReadStringArray(root, "safety_checks"),
            raw_response = content
        };
    }

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        var normalized = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
        return new Uri(new Uri(normalized, UriKind.Absolute), "chat/completions");
    }

    private static object BuildRequestPayload(AiAgentConfig sanitized, IReadOnlyList<ChatMessage> messages)
    {
        return new
        {
            model = sanitized.model,
            temperature = sanitized.temperature,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray()
        };
    }

    private static AiAgentConfig ValidateAndSanitizeConfig(AiAgentConfig config)
    {
        var sanitized = config.Sanitize();
        if (string.IsNullOrWhiteSpace(sanitized.base_url))
        {
            throw new InvalidOperationException("Base URL is required.");
        }

        if (string.IsNullOrWhiteSpace(sanitized.model))
        {
            throw new InvalidOperationException("Model is required.");
        }

        if (string.IsNullOrWhiteSpace(sanitized.api_key))
        {
            throw new InvalidOperationException("API key is required.");
        }

        return sanitized;
    }

    private static void EnsureSuccessResponse(ChatCompletionTrace responseTrace)
    {
        if (responseTrace.StatusCode < 200 || responseTrace.StatusCode >= 300)
        {
            throw new InvalidOperationException($"LLM request failed ({responseTrace.StatusCode}): {TrimForError(responseTrace.ResponseBody)}");
        }
    }

    private readonly record struct ChatMessage(string Role, string Content);

    private readonly record struct ChatCompletionTrace(int StatusCode, string ResponseBody);

    private static string ExtractMessageContent(string responseBody)
    {
        using var responseDocument = JsonDocument.Parse(responseBody);
        if (!responseDocument.RootElement.TryGetProperty("choices", out var choicesElement) ||
            choicesElement.ValueKind != JsonValueKind.Array ||
            choicesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"LLM response did not contain choices: {TrimForError(responseBody)}");
        }

        return ExtractMessageContentFromChoice(choicesElement[0]);
    }

    private static string ExtractMessageContentFromChoice(JsonElement choiceElement)
    {
        if (!choiceElement.TryGetProperty("message", out var messageElement))
        {
            throw new InvalidOperationException("LLM response choice did not contain message.");
        }

        if (!messageElement.TryGetProperty("content", out var contentElement))
        {
            throw new InvalidOperationException("LLM response message did not contain content.");
        }

        return contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(
                contentElement.EnumerateArray()
                    .Select(part => part.TryGetProperty("text", out var textElement) ? textElement.GetString() : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => throw new InvalidOperationException("LLM response content type was unsupported.")
        };
    }

    private static string ExtractJsonPayload(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                trimmed = trimmed[(firstLineEnd + 1)..];
            }

            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence];
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            throw new InvalidOperationException($"LLM response was not valid JSON: {TrimForError(content)}");
        }

        return trimmed[firstBrace..(lastBrace + 1)];
    }

    private static string ReadString(JsonElement element, string propertyName, string fallback = "")
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        return value.GetString() ?? fallback;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static bool ReadBool(JsonElement element, string propertyName, bool fallback)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            return fallback;
        }

        return value.GetBoolean();
    }

    private static string[] ReadStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string TrimForError(string value)
    {
        const int maxLength = 600;
        var trimmed = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return trimmed.Length <= maxLength ? trimmed : $"{trimmed[..maxLength]}...";
    }

    private static void WriteLlmExchangeWithDiagnostics(
        AiAgentConfig config,
        object payload,
        Action<string>? debugLogFailureSink)
    {
        if (AiDebugLogger.TryWriteLlmExchange(config, payload, out var error) ||
            string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        debugLogFailureSink?.Invoke($"Failed to append LLM debug log at {AiRuntimePaths.LlmDebugJsonlPath}: {error}");
    }
}
