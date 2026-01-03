using LearningLanguageBot.Infrastructure.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LearningLanguageBot.Infrastructure.Database.Configurations;

public class ReviewLogConfiguration : IEntityTypeConfiguration<ReviewLog>
{
    public void Configure(EntityTypeBuilder<ReviewLog> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.ReviewedAt);
    }
}
