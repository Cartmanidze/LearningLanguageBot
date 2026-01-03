namespace LearningLanguageBot.Core.Entities;

public class Card
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long UserId { get; set; }
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public List<Example> Examples { get; set; } = [];
    public string SourceLang { get; set; } = string.Empty;
    public string TargetLang { get; set; } = string.Empty;

    // SRS parameters
    public int Repetitions { get; set; }
    public double EaseFactor { get; set; } = 2.5;
    public int IntervalDays { get; set; }
    public DateTime NextReviewAt { get; set; } = DateTime.UtcNow;
    public bool IsLearned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public ICollection<ReviewLog> ReviewLogs { get; set; } = new List<ReviewLog>();
}

public class Example
{
    public string Original { get; set; } = string.Empty;
    public string Translated { get; set; } = string.Empty;
}
