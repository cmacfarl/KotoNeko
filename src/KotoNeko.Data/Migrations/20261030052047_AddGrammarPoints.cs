using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGrammarPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyGrammarCap",
                table: "StudySettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GrammarPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    GrammarListEntryId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Explanation = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Formation = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Formality = table.Column<int>(type: "int", nullable: false),
                    Contrasts = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mistakes = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Situations = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DraftedBy = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrammarPoints_GrammarList_GrammarListEntryId",
                        column: x => x.GrammarListEntryId,
                        principalTable: "GrammarList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GrammarPoints_StudyItems_Id",
                        column: x => x.Id,
                        principalTable: "StudyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GrammarExamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GrammarPointId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    English = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HasAudio = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarExamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrammarExamples_GrammarPoints_GrammarPointId",
                        column: x => x.GrammarPointId,
                        principalTable: "GrammarPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GrammarExampleAudios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GrammarExampleId = table.Column<int>(type: "int", nullable: false),
                    Audio = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarExampleAudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrammarExampleAudios_GrammarExamples_GrammarExampleId",
                        column: x => x.GrammarExampleId,
                        principalTable: "GrammarExamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StudySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DailyGrammarCap",
                value: 2);

            migrationBuilder.CreateIndex(
                name: "IX_GrammarExampleAudios_GrammarExampleId",
                table: "GrammarExampleAudios",
                column: "GrammarExampleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrammarExamples_GrammarPointId_SortOrder",
                table: "GrammarExamples",
                columns: new[] { "GrammarPointId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GrammarPoints_GrammarListEntryId",
                table: "GrammarPoints",
                column: "GrammarListEntryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GrammarExampleAudios");

            migrationBuilder.DropTable(
                name: "GrammarExamples");

            migrationBuilder.DropTable(
                name: "GrammarPoints");

            // Grammar points' shared StudyItems rows (and, by cascade, their SRS rows and
            // review logs) would otherwise be left with no derived row to load as.
            migrationBuilder.Sql("DELETE FROM `StudyItems` WHERE `Kind` = 2;");

            migrationBuilder.DropColumn(
                name: "DailyGrammarCap",
                table: "StudySettings");
        }
    }
}
