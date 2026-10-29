using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSentenceReadingsAndDictation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyDictationCap",
                table: "StudySettings",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.CreateTable(
                name: "DictationAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StudyItemId = table.Column<int>(type: "int", nullable: false),
                    Sentence = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Typed = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Partial = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Score = table.Column<double>(type: "double", nullable: false),
                    PracticedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DictationAttempts_StudyItems_StudyItemId",
                        column: x => x.StudyItemId,
                        principalTable: "StudyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SentenceReadings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TextHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Furigana = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Words = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEdited = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentenceReadings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StudySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DailyDictationCap",
                value: 10);

            migrationBuilder.CreateIndex(
                name: "IX_DictionaryEntries_Written",
                table: "DictionaryEntries",
                column: "Written");

            migrationBuilder.CreateIndex(
                name: "IX_DictationAttempts_PracticedAt",
                table: "DictationAttempts",
                column: "PracticedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DictationAttempts_StudyItemId",
                table: "DictationAttempts",
                column: "StudyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SentenceReadings_TextHash",
                table: "SentenceReadings",
                column: "TextHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DictationAttempts");

            migrationBuilder.DropTable(
                name: "SentenceReadings");

            migrationBuilder.DropIndex(
                name: "IX_DictionaryEntries_Written",
                table: "DictionaryEntries");

            migrationBuilder.DropColumn(
                name: "DailyDictationCap",
                table: "StudySettings");
        }
    }
}
