using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LearningLanguageBot.Features.Import.Services;

public class GeniusService
{
    private readonly HttpClient _httpClient;
    private readonly GeniusOptions _options;
    private readonly ILogger<GeniusService> _logger;

    private const string ApiBaseUrl = "https://api.genius.com";

    public GeniusService(
        HttpClient httpClient,
        IOptions<GeniusOptions> options,
        ILogger<GeniusService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.AccessToken}");
    }

    public async Task<List<SongSearchResult>> SearchSongsAsync(string query, int limit = 5, CancellationToken ct = default)
    {
        try
        {
            var url = $"{ApiBaseUrl}/search?q={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetStringAsync(url, ct);

            var json = JsonDocument.Parse(response);
            var hits = json.RootElement
                .GetProperty("response")
                .GetProperty("hits");

            var results = new List<SongSearchResult>();

            foreach (var hit in hits.EnumerateArray().Take(limit))
            {
                var song = hit.GetProperty("result");
                results.Add(new SongSearchResult
                {
                    Id = song.GetProperty("id").GetInt64(),
                    Title = song.GetProperty("title").GetString() ?? "",
                    Artist = song.GetProperty("primary_artist").GetProperty("name").GetString() ?? "",
                    Url = song.GetProperty("url").GetString() ?? "",
                    ThumbnailUrl = song.TryGetProperty("song_art_image_thumbnail_url", out var thumb)
                        ? thumb.GetString()
                        : null
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search songs on Genius: {Query}", query);
            return [];
        }
    }
}

public class SongSearchResult
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }

    public string DisplayName => $"{Artist} — {Title}";
}

public class GeniusOptions
{
    public const string SectionName = "Genius";
    public string AccessToken { get; set; } = string.Empty;
}
