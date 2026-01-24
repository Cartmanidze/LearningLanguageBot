namespace LearningLanguageBot.Infrastructure.Database.Models;

public class ReviewLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CardId { get; set; }

    /// <summary>
    /// FSRS Rating: Again=1, Hard=2, Good=3, Easy=4
    /// </summary>
    public int Rating { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;

    public Card Card { get; set; } = null!;
}