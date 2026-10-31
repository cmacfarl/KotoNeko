using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyListeningMinutes",
                table: "StudySettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ListeningScriptModel",
                table: "StudySettings",
                type: "varchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ListeningScripts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TitleEnglish = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Topic = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Question = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnswerPoints = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewWords = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Writer = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Flagged = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FlagNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningScripts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ListeningAnswers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ListeningScriptId = table.Column<int>(type: "int", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Answer = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Understood = table.Column<int>(type: "int", nullable: false),
                    VerdictJson = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PromptVersion = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningAnswers_ListeningScripts_ListeningScriptId",
                        column: x => x.ListeningScriptId,
                        principalTable: "ListeningScripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ListeningLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ListeningScriptId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Speaker = table.Column<string>(type: "varchar(1)", maxLength: 1, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    English = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HasAudio = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningLines_ListeningScripts_ListeningScriptId",
                        column: x => x.ListeningScriptId,
                        principalTable: "ListeningScripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ListeningLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ListeningScriptId = table.Column<int>(type: "int", nullable: false),
                    ListenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Seconds = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningLogs_ListeningScripts_ListeningScriptId",
                        column: x => x.ListeningScriptId,
                        principalTable: "ListeningScripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ListeningLineAudios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ListeningLineId = table.Column<int>(type: "int", nullable: false),
                    Audio = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningLineAudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningLineAudios_ListeningLines_ListeningLineId",
                        column: x => x.ListeningLineId,
                        principalTable: "ListeningLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StudySettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DailyListeningMinutes", "ListeningScriptModel" },
                values: new object[] { 13, "" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAnswers_ListeningScriptId_AnsweredAt",
                table: "ListeningAnswers",
                columns: new[] { "ListeningScriptId", "AnsweredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningLineAudios_ListeningLineId",
                table: "ListeningLineAudios",
                column: "ListeningLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningLines_ListeningScriptId_SortOrder",
                table: "ListeningLines",
                columns: new[] { "ListeningScriptId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningLogs_ListenedAt",
                table: "ListeningLogs",
                column: "ListenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningLogs_ListeningScriptId",
                table: "ListeningLogs",
                column: "ListeningScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningScripts_CreatedAt",
                table: "ListeningScripts",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListeningAnswers");

            migrationBuilder.DropTable(
                name: "ListeningLineAudios");

            migrationBuilder.DropTable(
                name: "ListeningLogs");

            migrationBuilder.DropTable(
                name: "ListeningLines");

            migrationBuilder.DropTable(
                name: "ListeningScripts");

            migrationBuilder.DropColumn(
                name: "DailyListeningMinutes",
                table: "StudySettings");

            migrationBuilder.DropColumn(
                name: "ListeningScriptModel",
                table: "StudySettings");
        }
    }
}
