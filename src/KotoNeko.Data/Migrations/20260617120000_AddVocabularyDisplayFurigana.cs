using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVocabularyDisplayFurigana : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows default to true to match the "on by default" intent.
            // (Change this to false if you want existing kanji items to keep being
            // quizzed on their reading.)
            migrationBuilder.AddColumn<bool>(
                name: "DisplayFurigana",
                table: "Vocabulary",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayFurigana",
                table: "Vocabulary");
        }
    }
}
