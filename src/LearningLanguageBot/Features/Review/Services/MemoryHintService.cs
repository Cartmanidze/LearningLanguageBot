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
        Ты помощник для изучения языков. Помоги запомнить слово через фонетическую ассоциацию.
        Создавай ЯРКИЕ, СМЕШНЫЕ или АБСУРДНЫЕ образы — такое запоминается лучше.
        Отвечай кратко и по делу.
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
            return string.Empty;

        // Return cached hint if available
        if (!string.IsNullOrEmpty(card.MemoryHint))
            return card.MemoryHint;

        // Generate new hint
        var hint = await GenerateHintAsync(card.Front, card.Back, card.SourceLang, card.TargetLang, ct);

        // Cache in database
        card.MemoryHint = hint;
        await _db.SaveChangesAsync(ct);

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
            Слово: "{translation}" ({targetLangName})
            Перевод: "{word}" ({sourceLangName})

            🔊 **Звучит как / Sounds like**:
            Найди созвучие на {sourceLangName}: "{translation}" ≈ [похожие слова/слоги]
            Разбей на части если нужно: "se-ren-di-pi-ty" → "сэр" + "Индия" + "типа"

            🎬 **Представь / Imagine**:
            Опиши ЯРКУЮ сцену (2-3 предложения), которая связывает:
            - созвучие с {sourceLangName}
            - значение "{word}"
            Сделай её смешной, абсурдной или эмоциональной!

            📝 **Запомни / Remember**:
            Одна формула-связка (5-10 слов):
            "[созвучие] → [образ] → {word}"
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
