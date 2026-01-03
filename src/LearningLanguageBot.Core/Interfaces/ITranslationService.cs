using LearningLanguageBot.Shared.Models;

namespace LearningLanguageBot.Core.Interfaces;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default);
}
