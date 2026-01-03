using LearningLanguageBot.Infrastructure.Database.Models;

namespace LearningLanguageBot.Features.Review.Services;

/// <summary>
/// Simplified SM-2 spaced repetition algorithm
/// </summary>
public static class SrsEngine
{
    private const double MinEaseFactor = 1.3;
    private const double MaxEaseFactor = 2.5;
    private const int LearnedThresholdDays = 21;

    public static void ProcessReview(Card card, bool knew)
    {
        if (knew)
        {
            ProcessCorrectAnswer(card);
        }
        else
        {
            ProcessIncorrectAnswer(card);
        }

        card.IsLearned = card.IntervalDays >= LearnedThresholdDays;
    }

    private static void ProcessCorrectAnswer(Card card)
    {
        card.Repetitions++;

        card.IntervalDays = card.Repetitions switch
        {
            1 => 1,
            2 => 3,
            _ => (int)Math.Round(card.IntervalDays * card.EaseFactor)
        };

        // Slightly increase ease factor for consistent correct answers
        if (card.Repetitions > 2)
        {
            card.EaseFactor = Math.Min(card.EaseFactor + 0.1, MaxEaseFactor);
        }

        card.NextReviewAt = DateTime.UtcNow.AddDays(card.IntervalDays);
    }

    private static void ProcessIncorrectAnswer(Card card)
    {
        card.Repetitions = 0;
        card.IntervalDays = 0;

        // Decrease ease factor
        card.EaseFactor = Math.Max(card.EaseFactor - 0.2, MinEaseFactor);

        // Show again in 1 minute (within same session)
        card.NextReviewAt = DateTime.UtcNow.AddMinutes(1);
    }

    public static IEnumerable<Card> GetCardsForReview(IEnumerable<Card> cards, int limit)
    {
        var now = DateTime.UtcNow;

        return cards
            .Where(c => c.NextReviewAt <= now)
            .OrderBy(c => c.NextReviewAt)           // Oldest first
            .ThenBy(c => c.EaseFactor)              // Harder cards first
            .Take(limit);
    }

    public static int CountDueCards(IEnumerable<Card> cards)
    {
        var now = DateTime.UtcNow;
        return cards.Count(c => c.NextReviewAt <= now);
    }
}
