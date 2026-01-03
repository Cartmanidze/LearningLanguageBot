using LearningLanguageBot.Infrastructure.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LearningLanguageBot.Infrastructure.Database.Configurations;

public class UserStatsConfiguration : IEntityTypeConfiguration<UserStats>
{
    public void Configure(EntityTypeBuilder<UserStats> builder)
    {
        builder.HasKey(s => s.UserId);

        builder.Property(s => s.WeeklyHistory)
            .HasColumnType("jsonb");
    }
}
