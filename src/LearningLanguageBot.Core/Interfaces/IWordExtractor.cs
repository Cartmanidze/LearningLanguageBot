using LearningLanguageBot.Shared.Models;

namespace LearningLanguageBot.Core.Interfaces;

public interface IWordExtractor
{
    Task<ExtractedWordsResult> ExtractWordsAsync(
        string text,
        string targetLang,
        IEnumerable<string> knownWords,
        CancellationToken ct = default);
}
