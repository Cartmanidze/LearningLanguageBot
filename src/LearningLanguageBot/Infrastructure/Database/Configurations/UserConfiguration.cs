using LearningLanguageBot.Infrastructure.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LearningLanguageBot.Infrastructure.Database.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.TelegramId);

        builder.Property(u => u.NativeLanguage)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(u => u.TargetLanguage)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(u => u.TimeZone)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(u => u.ReminderTimes)
            .HasColumnType("jsonb");

        builder.HasMany(u => u.Cards)
            .WithOne(c => c.User)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.Stats)
            .WithOne(s => s.User)
            .HasForeignKey<UserStats>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}