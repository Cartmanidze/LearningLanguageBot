namespace LearningLanguageBot.Infrastructure.Database.Models;

public class ReviewLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CardId { get; set; }
    public bool Knew { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;

    public Card Card { get; set; } = null!;
}