using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeSentences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KanaLength",
                table: "DictationAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PracticeSentenceId",
                table: "DictationAttempts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PracticeSentences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VocabularyId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    English = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HasAudio = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSentences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeSentences_Vocabulary_VocabularyId",
                        column: x => x.VocabularyId,
                        principalTable: "Vocabulary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PracticeSentenceAudios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PracticeSentenceId = table.Column<int>(type: "int", nullable: false),
                    Audio = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSentenceAudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeSentenceAudios_PracticeSentences_PracticeSentenceId",
                        column: x => x.PracticeSentenceId,
                        principalTable: "PracticeSentences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DictationAttempts_PracticeSentenceId",
                table: "DictationAttempts",
                column: "PracticeSentenceId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSentenceAudios_PracticeSentenceId",
                table: "PracticeSentenceAudios",
                column: "PracticeSentenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSentences_VocabularyId_Status",
                table: "PracticeSentences",
                columns: new[] { "VocabularyId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_DictationAttempts_PracticeSentences_PracticeSentenceId",
                table: "DictationAttempts",
                column: "PracticeSentenceId",
                principalTable: "PracticeSentences",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DictationAttempts_PracticeSentences_PracticeSentenceId",
                table: "DictationAttempts");

            migrationBuilder.DropTable(
                name: "PracticeSentenceAudios");

            migrationBuilder.DropTable(
                name: "PracticeSentences");

            migrationBuilder.DropIndex(
                name: "IX_DictationAttempts_PracticeSentenceId",
                table: "DictationAttempts");

            migrationBuilder.DropColumn(
                name: "KanaLength",
                table: "DictationAttempts");

            migrationBuilder.DropColumn(
                name: "PracticeSentenceId",
                table: "DictationAttempts");
        }
    }
}
