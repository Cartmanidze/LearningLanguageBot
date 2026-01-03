namespace LearningLanguageBot.Shared.Models;

public record ExtractedWordsResult(List<ExtractedWord> Words);

public record ExtractedWord(
    string Word,
    string Translation,
    string? Example = null
);
