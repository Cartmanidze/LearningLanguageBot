using LearningLanguageBot.Features.Cards.Services;
using LearningLanguageBot.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LearningLanguageBot.Features.Review.Services;

public class MemoryHintService
{
    private readonly OpenRouterClient _client;
    private readonly AppDbContext _db;
    private readonly ILogger<MemoryHintService> _logger;

    private const string SystemPrompt = """
        Создай мнемоническую подсказку для запоминания слова. Будь КРАТКИМ (3-4 строки максимум).
        Формат: созвучие → яркий образ → значение.
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
    public async Task<string> GetOrGenerateHintAsync(Guid cardId, CancellationToken ct = default)
    {
        var card = await _db.Cards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card == null)
        {
            _logger.LogWarning("Card not found: {CardId}", cardId);
            return string.Empty;
        }

        // Return cached hint if available
        if (!string.IsNullOrEmpty(card.MemoryHint))
        {
            _logger.LogInformation("Returning cached hint for {Word}, length={Length}", card.Back, card.MemoryHint.Length);
            return card.MemoryHint;
        }

        _logger.LogInformation("Generating new hint for {Word} (MemoryHint was null/empty)", card.Back);

        // Generate new hint
        var hint = await GenerateHintAsync(card.Front, card.Back, card.SourceLang, card.TargetLang, ct);

        // Cache in database
        card.MemoryHint = hint;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Saved hint for {Word}, length={Length}", card.Back, hint.Length);

        return hint;
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
            "{translation}" = "{word}"

            🔊 Созвучие: "{translation}" ≈ [слова на {sourceLangName}]
            🎬 Образ: [1 яркое предложение]
            📝 Формула: [созвучие] → [образ] → {word}
            """;

        try
        {
            // Use smaller token limit for concise hints
            return await _client.ChatAsync(SystemPrompt, userPrompt, maxTokens: 200, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate memory hint for: {Word}", word);
            return "Не удалось загрузить подсказку";
        }
    }

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
