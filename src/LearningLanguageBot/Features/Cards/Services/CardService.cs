using LearningLanguageBot.Infrastructure.Constants;
using LearningLanguageBot.Infrastructure.Database;
using LearningLanguageBot.Infrastructure.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace LearningLanguageBot.Features.Cards.Services;

public class CardService
{
    private readonly AppDbContext _db;
    private readonly ITranslationService _translationService;

    public CardService(AppDbContext db, ITranslationService translationService)
    {
        _db = db;
        _translationService = translationService;
    }

    public async Task<(Card? card, bool isDuplicate)> CreateCardFromTextAsync(
        long userId,
        string text,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user == null) return (null, false);

        // Detect language and determine direction
        var inputLang = Languages.DetectLanguage(text);
        var sourceLang = inputLang;
        var targetLang = inputLang == user.NativeLanguage ? user.TargetLanguage : user.NativeLanguage;

        // Check for duplicate
        var normalizedText = text.Trim().ToLowerInvariant();
        var existing = await _db.Cards
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Front.ToLower() == normalizedText, ct);

        if (existing != null)
        {
            return (existing, true);
        }

        // Get translation from LLM
        var translation = await _translationService.TranslateAsync(text, sourceLang, targetLang, ct);

        var card = new Card
        {
            UserId = userId,
            Front = text.Trim(),
            Back = FormatTranslation(translation.Translation, translation.Alternatives),
            Examples = translation.Examples.Select(e => new Example
            {
                Original = e.Original,
                Translated = e.Translated
            }).ToList(),
            SourceLang = sourceLang,
            TargetLang = targetLang,
            NextReviewAt = DateTime.UtcNow
        };

        _db.Cards.Add(card);
        await _db.SaveChangesAsync(ct);

        return (card, false);
    }

    public async Task<Card?> GetCardAsync(Guid cardId, CancellationToken ct = default)
    {
        return await _db.Cards.FindAsync([cardId], ct);
    }

    public async Task UpdateCardTranslationAsync(Guid cardId, string newTranslation, CancellationToken ct = default)
    {
        var card = await _db.Cards.FindAsync([cardId], ct);
        if (card != null)
        {
            card.Back = newTranslation;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteCardAsync(Guid cardId, CancellationToken ct = default)
    {
        var card = await _db.Cards.FindAsync([cardId], ct);
        if (card != null)
        {
            _db.Cards.Remove(card);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<Card>> GetCardsForReviewAsync(long userId, int limit, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Cards
            .Where(c => c.UserId == userId && c.NextReviewAt <= now)
            .OrderBy(c => c.NextReviewAt)
            .ThenBy(c => c.EaseFactor)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> GetDueCardsCountAsync(long userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Cards
            .CountAsync(c => c.UserId == userId && c.NextReviewAt <= now, ct);
    }

    private static string FormatTranslation(string main, List<string> alternatives)
    {
        if (alternatives.Count == 0) return main;
        return $"{main}, {string.Join(", ", alternatives)}";
    }
}
