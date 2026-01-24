using FSRS.Core.Enums;
using FSRS.Core.Interfaces;
using FSRS.Core.Models;
using AppCard = LearningLanguageBot.Infrastructure.Database.Models.Card;

namespace LearningLanguageBot.Features.Review.Services;

/// <summary>
/// FSRS (Free Spaced Repetition Scheduler) service.
/// Replaces the old SM-2 algorithm with a more accurate memory model.
/// </summary>
public class FsrsService
{
    private const int LearnedThresholdDays = 21;
    private readonly IScheduler _scheduler;

    public FsrsService(IScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// Process a card review with the given rating.
    /// </summary>
    public void ProcessReview(AppCard card, Rating rating)
    {
        var fsrsCard = ToFsrsCard(card);
        var (updatedCard, _) = _scheduler.ReviewCard(fsrsCard, rating);
        UpdateCardFromFsrs(card, updatedCard);
    }

    /// <summary>
    /// Get preview of next intervals for all 4 ratings (for UI buttons).
    /// </summary>
    public Dictionary<Rating, TimeSpan> GetNextIntervals(AppCard card)
    {
        var fsrsCard = ToFsrsCard(card);
        var now = DateTime.UtcNow;
        var result = new Dictionary<Rating, TimeSpan>();

        foreach (Rating rating in Enum.GetValues<Rating>())
        {
            var (preview, _) = _scheduler.ReviewCard(fsrsCard, rating, now);
            var interval = preview.Due - now;
            result[rating] = interval > TimeSpan.Zero ? interval : TimeSpan.Zero;
        }

        return result;
    }

    /// <summary>
    /// Format interval for display on button (e.g., "1м", "10м", "1д", "4д").
    /// </summary>
    public static string FormatInterval(TimeSpan interval)
    {
        if (interval.TotalMinutes < 1)
            return "<1м";
        if (interval.TotalMinutes < 60)
            return $"{(int)interval.TotalMinutes}м";
        if (interval.TotalHours < 24)
            return $"{(int)interval.TotalHours}ч";
        if (interval.TotalDays < 30)
            return $"{(int)interval.TotalDays}д";
        if (interval.TotalDays < 365)
            return $"{(int)(interval.TotalDays / 30)}мес";
        return $"{interval.TotalDays / 365:F1}г";
    }

    private static Card ToFsrsCard(AppCard card)
    {
        // Map app card state to FSRS state
        // App: 0=New, 1=Learning, 2=Review, 3=Relearning
        // FSRS.Core: Learning=0, Review=1, Relearning=2 (no "New" — new cards start as Learning with Step=0)
        var fsrsState = card.State switch
        {
            0 => State.Learning,  // New → Learning with step 0
            1 => State.Learning,
            2 => State.Review,
            3 => State.Relearning,
            _ => State.Learning
        };

        return new Card(
            cardId: null,
            state: fsrsState,
            step: card.State == 0 ? 0 : null,  // New cards start at step 0
            stability: card.Stability > 0 ? card.Stability : null,
            difficulty: card.Difficulty > 0 ? card.Difficulty : null,
            due: card.NextReviewAt,
            lastReview: card.LastReview ?? DateTime.UtcNow  // FSRS requires lastReview, use now for new cards
        );
    }

    private void UpdateCardFromFsrs(AppCard card, Card fsrsCard)
    {
        card.NextReviewAt = fsrsCard.Due;
        card.Stability = fsrsCard.Stability ?? 0;
        card.Difficulty = fsrsCard.Difficulty ?? 0.3;
        card.LastReview = fsrsCard.LastReview;
        card.Reps++;

        // Map FSRS state back to app state
        // FSRS.Core: Learning=0, Review=1, Relearning=2
        // App: 0=New, 1=Learning, 2=Review, 3=Relearning
        card.State = fsrsCard.State switch
        {
            State.Learning => 1,
            State.Review => 2,
            State.Relearning => 3,
            _ => 1
        };

        // Track lapses (relearning = forgotten)
        if (fsrsCard.State == State.Relearning)
        {
            card.Lapses++;
        }

        // Card is considered "learned" when stability >= 21 days
        card.IsLearned = (fsrsCard.Stability ?? 0) >= LearnedThresholdDays;
    }
}
