using System.Text.Json;
using LearningLanguageBot.Features.Cards.Models;
using Microsoft.Extensions.Logging;

namespace LearningLanguageBot.Features.Cards.Services;

public class TranslationService : ITranslationService
{
    private readonly OpenRouterClient _client;
    private readonly ILogger<TranslationService> _logger;

    private const string SystemPrompt = """
        Ты помощник для изучения языков.
        Переводи слова/фразы и давай примеры использования на ЦЕЛЕВОМ языке.
        Отвечай ТОЛЬКО валидным JSON без markdown-разметки.
        """;

    public TranslationService(OpenRouterClient client, ILogger<TranslationService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct = default)
    {
        var sourceLangName = GetLanguageName(sourceLang);
        var targetLangName = GetLanguageName(targetLang);

        var userPrompt = $"""
            Переведи слово/фразу и дай 2-3 примера использования НА ЦЕЛЕВОМ ЯЗЫКЕ ({targetLangName}).

            Текст: "{text}"
            Направление: {sourceLangName} → {targetLangName}

            ВАЖНО: Примеры должны быть ТОЛЬКО на {targetLangName}!

            Верни JSON в формате:
            {"{"}
              "translation": "основной перевод",
              "alternatives": ["альтернатива1", "альтернатива2"],
              "examples": [
                {"{"}"original": "пример на {targetLangName}"{"}"},
                {"{"}"original": "ещё пример на {targetLangName}"{"}"}
              ]
            {"}"}
            """;

        try
        {
            var response = await _client.ChatAsync(SystemPrompt, userPrompt, ct);
            return ParseTranslationResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed for: {Text}", text);
            throw;
        }
    }

    private static TranslationResult ParseTranslationResponse(string json)
    {
        // Remove potential markdown code blocks
        json = json.Trim();
        if (json.StartsWith("```"))
        {
            var lines = json.Split('\n');
            json = string.Join('\n', lines.Skip(1).Take(lines.Length - 2));
        }

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var translation = root.GetProperty("translation").GetString() ?? string.Empty;

        var alternatives = new List<string>();
        if (root.TryGetProperty("alternatives", out var altElement))
        {
            foreach (var alt in altElement.EnumerateArray())
            {
                alternatives.Add(alt.GetString() ?? string.Empty);
            }
        }

        var examples = new List<TranslationExample>();
        if (root.TryGetProperty("examples", out var exElement))
        {
            foreach (var ex in exElement.EnumerateArray())
            {
                var original = ex.GetProperty("original").GetString() ?? string.Empty;
                // translated is now optional (examples are only in target language)
                var translated = ex.TryGetProperty("translated", out var trProp)
                    ? trProp.GetString() ?? string.Empty
                    : string.Empty;
                examples.Add(new TranslationExample(original, translated));
            }
        }

        return new TranslationResult(translation, alternatives, examples);
    }

    private static string GetLanguageName(string code) => code switch
    {
        "ru" => "русский",
        "en" => "английский",
        _ => code
    };
}
