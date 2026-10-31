using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCasualVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CasualVersionId",
                table: "DictationAttempts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CasualVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PracticeSentenceId = table.Column<int>(type: "int", nullable: true),
                    GrammarExampleId = table.Column<int>(type: "int", nullable: true),
                    Original = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HasAudio = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Writer = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CasualVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CasualVersions_GrammarExamples_GrammarExampleId",
                        column: x => x.GrammarExampleId,
                        principalTable: "GrammarExamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CasualVersions_PracticeSentences_PracticeSentenceId",
                        column: x => x.PracticeSentenceId,
                        principalTable: "PracticeSentences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CasualVersionAudios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CasualVersionId = table.Column<int>(type: "int", nullable: false),
                    Audio = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CasualVersionAudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CasualVersionAudios_CasualVersions_CasualVersionId",
                        column: x => x.CasualVersionId,
                        principalTable: "CasualVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DictationAttempts_CasualVersionId",
                table: "DictationAttempts",
                column: "CasualVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CasualVersionAudios_CasualVersionId",
                table: "CasualVersionAudios",
                column: "CasualVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CasualVersions_GrammarExampleId",
                table: "CasualVersions",
                column: "GrammarExampleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CasualVersions_PracticeSentenceId",
                table: "CasualVersions",
                column: "PracticeSentenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CasualVersions_Status",
                table: "CasualVersions",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_DictationAttempts_CasualVersions_CasualVersionId",
                table: "DictationAttempts",
                column: "CasualVersionId",
                principalTable: "CasualVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DictationAttempts_CasualVersions_CasualVersionId",
                table: "DictationAttempts");

            migrationBuilder.DropTable(
                name: "CasualVersionAudios");

            migrationBuilder.DropTable(
                name: "CasualVersions");

            migrationBuilder.DropIndex(
                name: "IX_DictationAttempts_CasualVersionId",
                table: "DictationAttempts");

            migrationBuilder.DropColumn(
                name: "CasualVersionId",
                table: "DictationAttempts");
        }
    }
}
