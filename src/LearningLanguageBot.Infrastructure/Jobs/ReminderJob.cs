using LearningLanguageBot.Core.Entities;
using LearningLanguageBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LearningLanguageBot.Infrastructure.Jobs;

public class ReminderJob
{
    private readonly AppDbContext _db;
    private readonly ILogger<ReminderJob> _logger;

    public ReminderJob(AppDbContext db, ILogger<ReminderJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<User>> GetUsersForReminderAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var currentTime = TimeOnly.FromDateTime(now);
        var today = DateOnly.FromDateTime(now);

        // Get users who have a reminder time within the last minute
        // and haven't completed their daily goal
        var users = await _db.Users
            .Where(u => u.IsActive)
            .Where(u => u.TodayDate != today || u.TodayReviewed < u.DailyGoal)
            .ToListAsync(ct);

        return users.Where(u =>
        {
            // Check if current time matches any reminder time (within 1 minute window)
            return u.ReminderTimes.Any(rt =>
            {
                var diff = Math.Abs((currentTime.ToTimeSpan() - rt.ToTimeSpan()).TotalMinutes);
                return diff < 1;
            });
        }).ToList();
    }

    public async Task<int> GetDueCardsCountAsync(long userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Cards.CountAsync(c => c.UserId == userId && c.NextReviewAt <= now, ct);
    }

    public async Task MarkUserInactiveAsync(long userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user != null)
        {
            user.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Marked user {UserId} as inactive", userId);
        }
    }
}
