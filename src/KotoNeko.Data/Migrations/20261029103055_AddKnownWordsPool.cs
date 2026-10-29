using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKnownWordsPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DoNotUse",
                table: "Vocabulary",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "JlptLevel",
                table: "Vocabulary",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KnownWordStage",
                table: "StudySettings",
                type: "int",
                nullable: false,
                // Guru: 0 would be Locked, which would put every studied word in the pool.
                defaultValue: 5);

            // Words already asleep and never studied may be asleep because they
            // aren't known yet, so they don't count as known until the user says so.
            // Words studied and then put to sleep are left alone. Words seeded later
            // (and N5/N4 seeded words) are not affected: this runs once.
            migrationBuilder.Sql(
                "UPDATE `Vocabulary` v JOIN `StudyItems` s ON s.`Id` = v.`Id` " +
                "LEFT JOIN `SrsItems` r ON r.`StudyItemId` = v.`Id` " +
                "SET v.`DoNotUse` = 1 WHERE s.`IsAsleep` = 1 AND (r.`Id` IS NULL OR r.`Stage` = 0);");

            migrationBuilder.UpdateData(
                table: "StudySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "KnownWordStage",
                value: 5);

            migrationBuilder.CreateIndex(
                name: "IX_Vocabulary_JlptLevel",
                table: "Vocabulary",
                column: "JlptLevel");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vocabulary_JlptLevel",
                table: "Vocabulary");

            migrationBuilder.DropColumn(
                name: "DoNotUse",
                table: "Vocabulary");

            migrationBuilder.DropColumn(
                name: "JlptLevel",
                table: "Vocabulary");

            migrationBuilder.DropColumn(
                name: "KnownWordStage",
                table: "StudySettings");
        }
    }
}
