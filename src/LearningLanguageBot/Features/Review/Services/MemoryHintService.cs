using System.Text.RegularExpressions;
using LearningLanguageBot.Features.Cards.Services;
using LearningLanguageBot.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LearningLanguageBot.Features.Review.Services;

public record MemoryHintResult(string Hint, string? ImageKeyword);

public partial class MemoryHintService
{
    private readonly OpenRouterClient _client;
    private readonly AppDbContext _db;
    private readonly ILogger<MemoryHintService> _logger;

    private const string SystemPrompt = """
        Ты помощник для изучения языков. Помоги запомнить слово.
        Отвечай кратко и по делу. Используй emoji для структуры.
        """;

    public MemoryHintService(
        OpenRouterClient client,
        AppDbContext db,
        ILogger<MemoryHintService> logger)
    {
        _client = client;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Gets or generates a memory hint for the card.
    /// </summary>
    public async Task<MemoryHintResult> GetOrGenerateHintAsync(Guid cardId, CancellationToken ct = default)
    {
        var card = await _db.Cards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card == null)
            return new MemoryHintResult(string.Empty, null);

        // Return cached hint if available
        if (!string.IsNullOrEmpty(card.MemoryHint))
        {
            var (cachedHint, cachedKeyword) = ExtractImageKeyword(card.MemoryHint);
            return new MemoryHintResult(cachedHint, cachedKeyword);
        }

        // Generate new hint
        var rawHint = await GenerateHintAsync(card.Front, card.Back, card.SourceLang, card.TargetLang, ct);

        // Cache in database (with image keyword included)
        card.MemoryHint = rawHint;
        await _db.SaveChangesAsync(ct);

        var (hint, imageKeyword) = ExtractImageKeyword(rawHint);
        return new MemoryHintResult(hint, imageKeyword);
    }

    private async Task<string> GenerateHintAsync(
        string word,
        string translation,
        string sourceLang,
        string targetLang,
        CancellationToken ct)
    {
        var sourceLangName = GetLanguageName(sourceLang);
        var targetLangName = GetLanguageName(targetLang);

        var userPrompt = $"""
            Слово: "{translation}" ({targetLangName})
            Перевод: "{word}" ({sourceLangName})

            Напиши КРАТКО (каждый пункт 1-2 предложения) на ОБОИХ языках:

            📚 **Этимология / Etymology**:
            - {targetLangName}: откуда произошло слово "{translation}"
            - {sourceLangName}: перевод этимологии

            💬 **Использование / Usage**:
            - {targetLangName}: в каких ситуациях употребляется (формальное/неформальное)
            - {sourceLangName}: перевод

            🔄 **Синоним попроще / Simpler synonym**:
            - {targetLangName}: более простое/разговорное слово с тем же значением
            - {sourceLangName}: его перевод

            🧠 **Ассоциация / Mnemonic**:
            - {targetLangName}: мнемоника или образ для запоминания
            - {sourceLangName}: перевод ассоциации

            🖼️ **Image**: одно-два английских слова для поиска картинки, которая поможет запомнить это слово (конкретный образ, не абстрактный). Например для "intervene" → "handshake mediation", для "cruel" → "evil villain"
            """;

        try
        {
            return await _client.ChatAsync(SystemPrompt, userPrompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate memory hint for: {Word}", word);
            return "Не удалось загрузить подсказку";
        }
    }

    /// <summary>
    /// Extracts image keyword from hint and returns cleaned hint.
    /// </summary>
    private static (string hint, string? imageKeyword) ExtractImageKeyword(string rawHint)
    {
        var match = ImageKeywordRegex().Match(rawHint);
        if (!match.Success)
            return (rawHint, null);

        var imageKeyword = match.Groups[1].Value.Trim();
        var cleanHint = rawHint.Replace(match.Value, "").Trim();

        return (cleanHint, imageKeyword);
    }

    [GeneratedRegex(@"🖼️\s*\*{0,2}Image\*{0,2}:\s*(.+?)(?:\n|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageKeywordRegex();

    private static string GetLanguageName(string code) => code switch
    {
        "ru" => "русский",
        "en" => "английский",
        "de" => "немецкий",
        "fr" => "французский",
        "es" => "испанский",
        _ => code
    };
}
