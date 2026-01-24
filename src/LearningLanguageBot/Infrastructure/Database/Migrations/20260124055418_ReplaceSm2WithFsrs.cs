using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearningLanguageBot.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSm2WithFsrs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add new FSRS columns to Cards
            migrationBuilder.AddColumn<double>(
                name: "Stability",
                table: "Cards",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Difficulty",
                table: "Cards",
                type: "double precision",
                nullable: false,
                defaultValue: 0.3);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Lapses",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Reps",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReview",
                table: "Cards",
                type: "timestamp with time zone",
                nullable: true);

            // Step 2: Convert SM-2 data to FSRS format
            // Stability: Use IntervalDays as initial stability (minimum 1)
            // Difficulty: Convert EaseFactor (1.3-2.5) to Difficulty (0-1) inversely
            //             Higher EaseFactor = easier = lower Difficulty
            // State: 0 (New) if Repetitions=0, else 2 (Review)
            // Reps: Copy from Repetitions
            // LastReview: Calculate from NextReviewAt - IntervalDays
            migrationBuilder.Sql(@"
                UPDATE ""Cards"" SET
                    ""Stability"" = GREATEST(""IntervalDays"", 1),
                    ""Difficulty"" = LEAST(GREATEST(1.0 - (""EaseFactor"" - 1.3) / 1.2, 0.0), 1.0),
                    ""State"" = CASE WHEN ""Repetitions"" = 0 THEN 0 ELSE 2 END,
                    ""Reps"" = ""Repetitions"",
                    ""LastReview"" = CASE
                        WHEN ""IntervalDays"" > 0 THEN ""NextReviewAt"" - INTERVAL '1 day' * ""IntervalDays""
                        ELSE NULL
                    END
            ");

            // Step 3: Drop old SM-2 columns from Cards
            migrationBuilder.DropColumn(
                name: "EaseFactor",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "IntervalDays",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "Repetitions",
                table: "Cards");

            // Step 4: Add Rating column to ReviewLogs
            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "ReviewLogs",
                type: "integer",
                nullable: false,
                defaultValue: 3); // Default to Good

            // Step 5: Convert ReviewLogs.Knew to Rating
            // Knew=true → Rating=3 (Good), Knew=false → Rating=1 (Again)
            migrationBuilder.Sql(@"
                UPDATE ""ReviewLogs"" SET
                    ""Rating"" = CASE WHEN ""Knew"" THEN 3 ELSE 1 END
            ");

            // Step 6: Drop old Knew column
            migrationBuilder.DropColumn(
                name: "Knew",
                table: "ReviewLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add back old SM-2 columns to Cards
            migrationBuilder.AddColumn<double>(
                name: "EaseFactor",
                table: "Cards",
                type: "double precision",
                nullable: false,
                defaultValue: 2.5);

            migrationBuilder.AddColumn<int>(
                name: "IntervalDays",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Repetitions",
                table: "Cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Step 2: Convert FSRS data back to SM-2 format
            migrationBuilder.Sql(@"
                UPDATE ""Cards"" SET
                    ""EaseFactor"" = 2.5 - ""Difficulty"" * 1.2,
                    ""IntervalDays"" = CAST(""Stability"" AS INTEGER),
                    ""Repetitions"" = ""Reps""
            ");

            // Step 3: Drop FSRS columns from Cards
            migrationBuilder.DropColumn(
                name: "Stability",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "Lapses",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "Reps",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "LastReview",
                table: "Cards");

            // Step 4: Add back Knew column to ReviewLogs
            migrationBuilder.AddColumn<bool>(
                name: "Knew",
                table: "ReviewLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Step 5: Convert Rating back to Knew
            migrationBuilder.Sql(@"
                UPDATE ""ReviewLogs"" SET
                    ""Knew"" = ""Rating"" >= 3
            ");

            // Step 6: Drop Rating column
            migrationBuilder.DropColumn(
                name: "Rating",
                table: "ReviewLogs");
        }
    }
}
