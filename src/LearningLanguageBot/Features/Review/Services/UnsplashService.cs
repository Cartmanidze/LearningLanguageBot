using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LearningLanguageBot.Features.Review.Services;

public class UnsplashService
{
    private readonly HttpClient _httpClient;
    private readonly UnsplashOptions _options;
    private readonly ILogger<UnsplashService> _logger;

    public UnsplashService(
        HttpClient httpClient,
        IOptions<UnsplashOptions> options,
        ILogger<UnsplashService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.unsplash.com/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Client-ID {_options.AccessKey}");
    }

    /// <summary>
    /// Search for an image by keyword and return the URL.
    /// </summary>
    public async Task<string?> SearchImageAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AccessKey))
        {
            _logger.LogWarning("Unsplash API key not configured");
            return null;
        }

        try
        {
            _logger.LogInformation("Searching Unsplash for: {Keyword}", keyword);

            var response = await _httpClient.GetFromJsonAsync<UnsplashSearchResponse>(
                $"search/photos?query={Uri.EscapeDataString(keyword)}&per_page=1&orientation=squarish",
                ct);

            var photo = response?.Results?.FirstOrDefault();
            if (photo == null)
            {
                _logger.LogInformation("No image found for keyword: {Keyword}", keyword);
                return null;
            }

            _logger.LogInformation("Found image for {Keyword}: {AltDescription}", keyword, photo.AltDescription);

            // Use small size for Telegram (400px)
            return photo.Urls?.Small;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Unsplash for: {Keyword}", keyword);
            return null;
        }
    }
}

public class UnsplashOptions
{
    public const string SectionName = "Unsplash";

    public string AccessKey { get; set; } = string.Empty;
}

public class UnsplashSearchResponse
{
    [JsonPropertyName("results")]
    public List<UnsplashPhoto>? Results { get; set; }
}

public class UnsplashPhoto
{
    [JsonPropertyName("urls")]
    public UnsplashUrls? Urls { get; set; }

    [JsonPropertyName("alt_description")]
    public string? AltDescription { get; set; }
}

public class UnsplashUrls
{
    [JsonPropertyName("small")]
    public string? Small { get; set; }

    [JsonPropertyName("thumb")]
    public string? Thumb { get; set; }

    [JsonPropertyName("regular")]
    public string? Regular { get; set; }
}
