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
        var isNativeInput = inputLang == user.NativeLanguage;

        // Get translation from LLM
        // Examples are ALWAYS in target language (the language user is learning)
        var translation = await _translationService.TranslateAsync(
            text,
            inputLang,
            isNativeInput ? user.TargetLanguage : user.NativeLanguage,
            user.TargetLanguage, // examples always in target language
            ct);

        // Card structure: Front = Native (Russian), Back = Target (English)
        // Examples: Only in target language (English)
        string front, back;
        if (isNativeInput)
        {
            // User sent Russian → Front = Russian, Back = English translation
            front = text.Trim();
            back = FormatTranslation(translation.Translation, translation.Alternatives);
        }
        else
        {
            // User sent English → Front = Russian translation, Back = English original
            front = FormatTranslation(translation.Translation, translation.Alternatives);
            back = text.Trim();
        }

        // Check for duplicate by Front (native language)
        var normalizedFront = front.ToLowerInvariant();
        var existing = await _db.Cards
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Front.ToLower() == normalizedFront, ct);

        if (existing != null)
        {
            return (existing, true);
        }

        var card = new Card
        {
            UserId = userId,
            Front = front,
            Back = back,
            Examples = translation.Examples.Select(e => new Example
            {
                Original = e.Original,  // Now only target language
                Translated = string.Empty  // Not used anymore
            }).ToList(),
            SourceLang = user.NativeLanguage,
            TargetLang = user.TargetLanguage,
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
            .ThenByDescending(c => c.Difficulty)  // Harder cards first (higher difficulty)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> GetDueCardsCountAsync(long userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Cards
            .CountAsync(c => c.UserId == userId && c.NextReviewAt <= now, ct);
    }

    public async Task<(List<Card> cards, int totalCount)> GetUserCardsAsync(
        long userId,
        string? searchQuery,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Cards.Where(c => c.UserId == userId);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.Trim().ToLower();
            query = query.Where(c =>
                c.Front.ToLower().Contains(search) ||
                c.Back.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(ct);

        var cards = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (cards, totalCount);
    }

    public async Task<int> GetUserCardsCountAsync(long userId, CancellationToken ct = default)
    {
        return await _db.Cards.CountAsync(c => c.UserId == userId, ct);
    }

    private static string FormatTranslation(string main, List<string> alternatives)
    {
        if (alternatives.Count == 0) return main;
        return $"{main}, {string.Join(", ", alternatives)}";
    }
}
