using LearningLanguageBot.Infrastructure.Database;
using LearningLanguageBot.Infrastructure.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace LearningLanguageBot.Features.Onboarding.Services;

public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetUserAsync(long telegramId, CancellationToken ct = default)
    {
        return await _db.Users.FindAsync([telegramId], ct);
    }

    public async Task<User> GetOrCreateUserAsync(long telegramId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([telegramId], ct);
        if (user != null) return user;

        user = new User
        {
            TelegramId = telegramId,
            TodayDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        _db.Users.Add(user);

        var stats = new UserStats { UserId = telegramId };
        _db.UserStats.Add(stats);

        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateUserSettingsAsync(
        long telegramId,
        string? targetLanguage = null,
        ReviewMode? reviewMode = null,
        int? dailyGoal = null,
        List<TimeOnly>? reminderTimes = null,
        string? timeZone = null,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([telegramId], ct);
        if (user == null) return;

        if (targetLanguage != null) user.TargetLanguage = targetLanguage;
        if (reviewMode != null) user.ReviewMode = reviewMode.Value;
        if (dailyGoal != null) user.DailyGoal = dailyGoal.Value;
        if (reminderTimes != null) user.ReminderTimes = reminderTimes;
        if (timeZone != null) user.TimeZone = timeZone;

        await _db.SaveChangesAsync(ct);
    }

    public async Task IncrementTodayReviewedAsync(long telegramId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([telegramId], ct);
        if (user == null) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (user.TodayDate != today)
        {
            user.TodayDate = today;
            user.TodayReviewed = 0;
        }

        user.TodayReviewed++;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(int reviewed, int goal)> GetTodayProgressAsync(long telegramId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([telegramId], ct);
        if (user == null) return (0, 20);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reviewed = user.TodayDate == today ? user.TodayReviewed : 0;
        return (reviewed, user.DailyGoal);
    }

    public async Task<List<User>> GetUsersForReminderAsync(TimeOnly time, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return await _db.Users
            .Where(u => u.IsActive && u.ReminderTimes.Contains(time))
            .Where(u => u.TodayDate != today || u.TodayReviewed < u.DailyGoal)
            .ToListAsync(ct);
    }
}
