namespace LearningLanguageBot.Core.Entities;

public class User
{
    public long TelegramId { get; set; }
    public string NativeLanguage { get; set; } = "ru";
    public string TargetLanguage { get; set; } = "en";
    public int DailyGoal { get; set; } = 20;
    public ReviewMode ReviewMode { get; set; } = ReviewMode.Reveal;
    public List<TimeOnly> ReminderTimes { get; set; } = [new(9, 0), new(14, 0), new(20, 0)];
    public string TimeZone { get; set; } = "Europe/Moscow";
    public int TodayReviewed { get; set; }
    public DateOnly TodayDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Card> Cards { get; set; } = new List<Card>();
    public UserStats? Stats { get; set; }
}

public enum ReviewMode
{
    Reveal,  // "Вспоминать" - показать и оценить
    Typing   // "Печатать" - ввести перевод
}
