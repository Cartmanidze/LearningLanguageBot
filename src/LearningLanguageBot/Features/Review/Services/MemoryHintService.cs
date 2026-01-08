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
