using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewSecondOpinions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContrastPattern",
                table: "GrammarReviewAttempts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DisputeComment",
                table: "GrammarReviewAttempts",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DisputeModel",
                table: "GrammarReviewAttempts",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DisputeVerdictJson",
                table: "GrammarReviewAttempts",
                type: "text",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "Disputed",
                table: "GrammarReviewAttempts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Overturned",
                table: "GrammarReviewAttempts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecondModel",
                table: "GrammarReviewAttempts",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SecondVerdictJson",
                table: "GrammarReviewAttempts",
                type: "text",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Master's contrast sentences would pass for sentences with the point itself.
            migrationBuilder.Sql("DELETE FROM `GrammarReviewAttempts` WHERE `ContrastPattern` IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "ContrastPattern",
                table: "GrammarReviewAttempts");

            migrationBuilder.DropColumn(
                name: "DisputeComment",
                table: "GrammarReviewAttempts");

            migrationBuilder.DropColumn(
                name: "DisputeModel",
                table: "GrammarReviewAttempts");

            migrationBuilder.DropColumn(
                name: "DisputeVerdictJson",
                table: "GrammarReviewAttempts");

            migrationBuilder.DropColumn(
                name: "Disputed",
                table: "GrammarReviewAttempts");

            migrationBuilder.DropColumn(
                name: "Overturned",
                table: "GrammarReviewAttempts");

            migrationBuilder.DropColumn(
                name: "SecondModel",
                table: "GrammarReviewAttempts");

            migrationBuilder.DropColumn(
                name: "SecondVerdictJson",
                table: "GrammarReviewAttempts");
        }
    }
}
