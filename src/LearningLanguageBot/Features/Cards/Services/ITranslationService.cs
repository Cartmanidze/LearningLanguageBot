using LearningLanguageBot.Features.Cards.Models;

namespace LearningLanguageBot.Features.Cards.Services;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct = default);
}
