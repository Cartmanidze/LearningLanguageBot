using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace LearningLanguageBot.Features.Import.Services;

public class ContentFetcherService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ContentFetcherService> _logger;

    public ContentFetcherService(HttpClient httpClient, ILogger<ContentFetcherService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public async Task<ContentResult> FetchContentAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLower();

            // Detect content type
            var contentType = DetectContentType(host, url);

            var html = await _httpClient.GetStringAsync(url, ct);
            var text = ExtractText(html, contentType);
            var title = ExtractTitle(html);

            return new ContentResult
            {
                Success = true,
                Title = title,
                Text = text,
                ContentType = contentType,
                SourceUrl = url
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch content from {Url}", url);
            return new ContentResult
            {
                Success = false,
                Error = "Не удалось загрузить контент. Проверь ссылку."
            };
        }
    }

    private static ContentType DetectContentType(string host, string url)
    {
        // Song lyrics sites
        if (host.Contains("genius.com") ||
            host.Contains("azlyrics.com") ||
            host.Contains("lyrics.com") ||
            host.Contains("songlyrics.com") ||
            host.Contains("musixmatch.com"))
        {
            return ContentType.SongLyrics;
        }

        // News/articles
        if (host.Contains("medium.com") ||
            host.Contains("bbc.com") ||
            host.Contains("cnn.com") ||
            host.Contains("nytimes.com") ||
            host.Contains("theguardian.com") ||
            host.Contains("wikipedia.org"))
        {
            return ContentType.Article;
        }

        return ContentType.Article; // Default
    }

    private static string ExtractText(string html, ContentType contentType)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove scripts, styles, etc.
        doc.DocumentNode.SelectNodes("//script|//style|//nav|//header|//footer|//aside")
            ?.ToList()
            .ForEach(n => n.Remove());

        string text;

        if (contentType == ContentType.SongLyrics)
        {
            // Try to find lyrics container
            var lyricsNode = doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class, 'lyrics')]" +
                "|//div[contains(@class, 'Lyrics')]" +
                "|//div[@data-lyrics-container]" +
                "|//div[contains(@class, 'lyric-body')]" +
                "|//pre");

            text = lyricsNode != null
                ? CleanText(lyricsNode.InnerText)
                : CleanText(doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? "");
        }
        else
        {
            // Article - try to find main content
            var articleNode = doc.DocumentNode.SelectSingleNode(
                "//article" +
                "|//main" +
                "|//div[contains(@class, 'article')]" +
                "|//div[contains(@class, 'content')]" +
                "|//div[contains(@class, 'post')]");

            text = articleNode != null
                ? CleanText(articleNode.InnerText)
                : CleanText(doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? "");
        }

        return text;
    }

    private static string ExtractTitle(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
        {
            return CleanText(titleNode.InnerText);
        }

        var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
        return h1Node != null ? CleanText(h1Node.InnerText) : "Untitled";
    }

    private static string CleanText(string text)
    {
        // Decode HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Remove extra whitespace
        text = Regex.Replace(text, @"\s+", " ");

        return text.Trim();
    }
}

public class ContentResult
{
    public bool Success { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public ContentType ContentType { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public enum ContentType
{
    Article,
    SongLyrics,
    PlainText
}
