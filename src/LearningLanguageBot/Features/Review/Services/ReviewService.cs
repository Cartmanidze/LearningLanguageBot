using FSRS.Core.Enums;
using LearningLanguageBot.Infrastructure.Database;
using LearningLanguageBot.Infrastructure.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace LearningLanguageBot.Features.Review.Services;

public class ReviewService
{
    private readonly AppDbContext _db;
    private readonly FsrsService _fsrsService;

    public ReviewService(AppDbContext db, FsrsService fsrsService)
    {
        _db = db;
        _fsrsService = fsrsService;
    }

    /// <summary>
    /// Process a card review with FSRS rating.
    /// </summary>
    public async Task ProcessReviewAsync(Guid cardId, Rating rating, CancellationToken ct = default)
    {
        var card = await _db.Cards.FindAsync([cardId], ct);
        if (card == null) return;

        _fsrsService.ProcessReview(card, rating);

        var log = new ReviewLog
        {
            CardId = cardId,
            Rating = (int)rating
        };

        _db.ReviewLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get preview of next intervals for all ratings (for UI).
    /// </summary>
    public async Task<Dictionary<Rating, TimeSpan>> GetNextIntervalsAsync(Guid cardId, CancellationToken ct = default)
    {
        var card = await _db.Cards.FindAsync([cardId], ct);
        if (card == null)
            return new Dictionary<Rating, TimeSpan>();

        return _fsrsService.GetNextIntervals(card);
    }

    public async Task<UserStats?> GetUserStatsAsync(long userId, CancellationToken ct = default)
    {
        return await _db.UserStats.FindAsync([userId], ct);
    }

    public async Task UpdateStatsAfterReviewAsync(long userId, CancellationToken ct = default)
    {
        var stats = await _db.UserStats.FindAsync([userId], ct);
        if (stats == null) return;

        stats.LastActivityAt = DateTime.UtcNow;

        // Update weekly history
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayActivity = stats.WeeklyHistory.FirstOrDefault(d => d.Date == today);
        if (todayActivity == null)
        {
            todayActivity = new DailyActivity { Date = today };
            stats.WeeklyHistory.Add(todayActivity);
        }
        todayActivity.CardsReviewed++;

        // Keep only last 7 days
        stats.WeeklyHistory = stats.WeeklyHistory
            .Where(d => d.Date >= today.AddDays(-6))
            .OrderBy(d => d.Date)
            .ToList();

        // Update card counts
        stats.TotalCards = await _db.Cards.CountAsync(c => c.UserId == userId, ct);
        stats.LearnedCards = await _db.Cards.CountAsync(c => c.UserId == userId && c.IsLearned, ct);

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateStreakAsync(long userId, CancellationToken ct = default)
    {
        var stats = await _db.UserStats.FindAsync([userId], ct);
        var user = await _db.Users.FindAsync([userId], ct);
        if (stats == null || user == null) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Check if daily goal is reached
        if (user.TodayDate == today && user.TodayReviewed >= user.DailyGoal)
        {
            var todayActivity = stats.WeeklyHistory.FirstOrDefault(d => d.Date == today);
            if (todayActivity != null)
            {
                todayActivity.GoalReached = true;
            }

            // Update streak
            var yesterday = today.AddDays(-1);
            var yesterdayActivity = stats.WeeklyHistory.FirstOrDefault(d => d.Date == yesterday);

            if (yesterdayActivity?.GoalReached == true || stats.CurrentStreak == 0)
            {
                stats.CurrentStreak++;
                stats.LongestStreak = Math.Max(stats.LongestStreak, stats.CurrentStreak);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
