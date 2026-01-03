namespace LearningLanguageBot.Features.Cards.Models;

public record TranslationResult(
    string Translation,
    List<string> Alternatives,
    List<TranslationExample> Examples
);

public record TranslationExample(string Original, string Translated);
