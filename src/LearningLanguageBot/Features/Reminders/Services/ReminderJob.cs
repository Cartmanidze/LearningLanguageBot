using LearningLanguageBot.Infrastructure.Database;
using LearningLanguageBot.Infrastructure.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LearningLanguageBot.Features.Reminders.Services;

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
        var utcNow = DateTime.UtcNow;

        // Get users who haven't completed their daily goal
        var users = await _db.Users
            .Where(u => u.IsActive)
            .ToListAsync(ct);

        return users.Where(u =>
        {
            // Convert UTC to user's local time
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(u.TimeZone);
            }
            catch
            {
                // Fallback to Moscow if timezone is invalid
                tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
            }

            var userLocalTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
            var userToday = DateOnly.FromDateTime(userLocalTime);
            var userCurrentTime = TimeOnly.FromDateTime(userLocalTime);

            // Skip if user already completed daily goal today (in their timezone)
            if (u.TodayDate == userToday && u.TodayReviewed >= u.DailyGoal)
                return false;

            // Check if current local time matches any reminder time (within 30 second window)
            return u.ReminderTimes.Any(rt =>
            {
                var diff = Math.Abs((userCurrentTime.ToTimeSpan() - rt.ToTimeSpan()).TotalMinutes);
                return diff < 0.5;
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
