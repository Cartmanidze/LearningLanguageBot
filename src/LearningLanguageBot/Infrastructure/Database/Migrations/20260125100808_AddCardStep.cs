using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearningLanguageBot.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCardStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Step",
                table: "Cards",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Step",
                table: "Cards");
        }
    }
}
