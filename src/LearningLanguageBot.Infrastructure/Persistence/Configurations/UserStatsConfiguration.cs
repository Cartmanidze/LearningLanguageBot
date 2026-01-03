using LearningLanguageBot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LearningLanguageBot.Infrastructure.Persistence.Configurations;

public class UserStatsConfiguration : IEntityTypeConfiguration<UserStats>
{
    public void Configure(EntityTypeBuilder<UserStats> builder)
    {
        builder.HasKey(s => s.UserId);

        builder.Property(s => s.WeeklyHistory)
            .HasColumnType("jsonb");
    }
}
