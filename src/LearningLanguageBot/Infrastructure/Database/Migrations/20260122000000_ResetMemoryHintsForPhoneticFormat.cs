using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearningLanguageBot.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ResetMemoryHintsForPhoneticFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reset all MemoryHint values to force regeneration with new phonetic format
            migrationBuilder.Sql("UPDATE \"Cards\" SET \"MemoryHint\" = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot restore old hints - they will be regenerated on demand
        }
    }
}
