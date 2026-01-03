namespace LearningLanguageBot.Core.Entities;

public class UserStats
{
    public long UserId { get; set; }
    public int TotalCards { get; set; }
    public int LearnedCards { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public List<DailyActivity> WeeklyHistory { get; set; } = [];

    public User User { get; set; } = null!;
}

public class DailyActivity
{
    public DateOnly Date { get; set; }
    public int CardsReviewed { get; set; }
    public bool GoalReached { get; set; }
}
