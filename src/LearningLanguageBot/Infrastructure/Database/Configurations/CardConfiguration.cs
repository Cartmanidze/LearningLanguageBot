using LearningLanguageBot.Infrastructure.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LearningLanguageBot.Infrastructure.Database.Configurations;

public class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Front)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(c => c.Back)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(c => c.SourceLang)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.TargetLang)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.Examples)
            .HasColumnType("jsonb");

        builder.HasIndex(c => new { c.UserId, c.Front })
            .IsUnique();

        builder.HasIndex(c => c.NextReviewAt);

        builder.HasMany(c => c.ReviewLogs)
            .WithOne(r => r.Card)
            .HasForeignKey(r => r.CardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}