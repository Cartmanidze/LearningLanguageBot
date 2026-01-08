using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearningLanguageBot.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMemoryHintToCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MemoryHint",
                table: "Cards",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MemoryHint",
                table: "Cards");
        }
    }
}
