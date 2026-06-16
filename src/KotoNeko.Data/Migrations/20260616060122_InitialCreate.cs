using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SourceMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceMaterials", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Vocabulary",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Japanese = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reading = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Meaning = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlternateReadings = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlternateMeanings = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContextSentence = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Memo = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerbClass = table.Column<int>(type: "int", nullable: false),
                    IsAsleep = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SourceMaterialId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vocabulary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vocabulary_SourceMaterials_SourceMaterialId",
                        column: x => x.SourceMaterialId,
                        principalTable: "SourceMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Conjugations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VocabularyId = table.Column<int>(type: "int", nullable: false),
                    Form = table.Column<int>(type: "int", nullable: false),
                    Polarity = table.Column<int>(type: "int", nullable: false),
                    ExpectedKana = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conjugations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conjugations_Vocabulary_VocabularyId",
                        column: x => x.VocabularyId,
                        principalTable: "Vocabulary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReviewLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VocabularyId = table.Column<int>(type: "int", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    QuestionType = table.Column<int>(type: "int", nullable: false),
                    ConjugationForm = table.Column<int>(type: "int", nullable: true),
                    ConjugationPolarity = table.Column<int>(type: "int", nullable: true),
                    WasCorrect = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StageAtReview = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewLogs_Vocabulary_VocabularyId",
                        column: x => x.VocabularyId,
                        principalTable: "Vocabulary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SrsItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VocabularyId = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    NextReviewAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UnlockedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    BurnedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    IncorrectCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SrsItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SrsItems_Vocabulary_VocabularyId",
                        column: x => x.VocabularyId,
                        principalTable: "Vocabulary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Conjugations_VocabularyId_Form_Polarity",
                table: "Conjugations",
                columns: new[] { "VocabularyId", "Form", "Polarity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLogs_ReviewedAt",
                table: "ReviewLogs",
                column: "ReviewedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLogs_VocabularyId",
                table: "ReviewLogs",
                column: "VocabularyId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLogs_WasCorrect",
                table: "ReviewLogs",
                column: "WasCorrect");

            migrationBuilder.CreateIndex(
                name: "IX_SourceMaterials_Name",
                table: "SourceMaterials",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SrsItems_NextReviewAt",
                table: "SrsItems",
                column: "NextReviewAt");

            migrationBuilder.CreateIndex(
                name: "IX_SrsItems_Stage",
                table: "SrsItems",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_SrsItems_VocabularyId",
                table: "SrsItems",
                column: "VocabularyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vocabulary_IsAsleep",
                table: "Vocabulary",
                column: "IsAsleep");

            migrationBuilder.CreateIndex(
                name: "IX_Vocabulary_SourceMaterialId",
                table: "Vocabulary",
                column: "SourceMaterialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Conjugations");

            migrationBuilder.DropTable(
                name: "ReviewLogs");

            migrationBuilder.DropTable(
                name: "SrsItems");

            migrationBuilder.DropTable(
                name: "Vocabulary");

            migrationBuilder.DropTable(
                name: "SourceMaterials");
        }
    }
}
