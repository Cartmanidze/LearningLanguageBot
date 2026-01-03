using System.Text.Json;
using LearningLanguageBot.Features.Cards.Services;
using Microsoft.Extensions.Logging;

namespace LearningLanguageBot.Features.Import.Services;

public class WordExtractorService
{
    private readonly OpenRouterClient _client;
    private readonly ILogger<WordExtractorService> _logger;

    private const string SystemPrompt = """
        Ты помощник для изучения английского языка.
        Извлекай полезные слова и фразы для изучения.
        Отвечай ТОЛЬКО валидным JSON без markdown-разметки.
        """;

    public WordExtractorService(OpenRouterClient client, ILogger<WordExtractorService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<ExtractedWord>> ExtractWordsAsync(
        string text,
        string userLevel,
        int maxWords,
        CancellationToken ct = default)
    {
        // Truncate text if too long (LLM context limit)
        var truncatedText = text.Length > 8000 ? text[..8000] + "..." : text;

        var userPrompt = $$"""
            Проанализируй текст и извлеки {{maxWords}} наиболее полезных английских слов/фраз для изучения.

            Критерии выбора:
            - Уровень сложности: {{userLevel}}
            - Выбирай интересные, часто используемые слова
            - Избегай простых слов (the, a, is, are, to, and, etc.)
            - Включай идиомы и полезные фразы если есть
            - Слова должны быть на АНГЛИЙСКОМ языке

            Текст:
            ---
            {{truncatedText}}
            ---

            Верни JSON массив:
            [
              {"word": "английское слово", "context": "короткий пример из текста или типичное использование"},
              {"word": "another word", "context": "example usage"}
            ]
            """;

        try
        {
            var response = await _client.ChatAsync(SystemPrompt, userPrompt, ct);
            return ParseExtractedWords(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Word extraction failed");
            return [];
        }
    }

    private static List<ExtractedWord> ParseExtractedWords(string json)
    {
        // Remove potential markdown code blocks
        json = json.Trim();
        if (json.StartsWith("```"))
        {
            var lines = json.Split('\n');
            json = string.Join('\n', lines.Skip(1).Take(lines.Length - 2));
        }

        try
        {
            var words = JsonSerializer.Deserialize<List<ExtractedWord>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return words ?? [];
        }
        catch
        {
            return [];
        }
    }
}

public class ExtractedWord
{
    public string Word { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}
