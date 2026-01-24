using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LearningLanguageBot.Features.Cards.Services;

public class OpenRouterClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<OpenRouterClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenRouterClient(
        HttpClient httpClient,
        IOptions<OpenRouterOptions> options,
        ILogger<OpenRouterClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        _httpClient.DefaultRequestHeaders.Add("X-Title", _options.SiteName);
    }

    public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        => await ChatAsync(systemPrompt, userPrompt, maxTokens: null, ct);

    public async Task<string> ChatAsync(string systemPrompt, string userPrompt, int? maxTokens, CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = _options.Model,
            Messages =
            [
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            ],
            MaxTokens = maxTokens ?? _options.MaxTokens,
            Temperature = _options.Temperature
        };

        var sw = Stopwatch.StartNew();

        try
        {
            // Use ResponseHeadersRead for earlier access to response stream
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/chat/completions")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };

            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var headersTime = sw.ElapsedMilliseconds;

            // Read and deserialize response body (this is where LLM streaming happens)
            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

            _logger.LogInformation(
                "LLM response: headers={HeadersMs}ms, total={TotalMs}ms, length={Length}chars",
                headersTime, sw.ElapsedMilliseconds, content.Length);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenRouter API call failed after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }
}

public class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string Model { get; set; } = "deepseek/deepseek-chat";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.3;
    public string SiteName { get; set; } = "LearningLanguageBot";
}

public class ChatRequest
{
    public string Model { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = [];
    public int MaxTokens { get; set; }
    public double Temperature { get; set; }
}

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ChatResponse
{
    public List<ChatChoice>? Choices { get; set; }
}

public class ChatChoice
{
    public ChatMessage? Message { get; set; }
}
