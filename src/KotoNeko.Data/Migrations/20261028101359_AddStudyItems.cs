using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <summary>
    /// Introduces the table-per-type StudyItems base table. Every existing
    /// Vocabulary row gets a StudyItems row with the same id, so SrsItems and
    /// ReviewLogs keep their values and only have their column renamed and their
    /// foreign key re-pointed. IsAsleep and CreatedAt move onto StudyItems.
    /// Hand-edited from the scaffold, which dropped those columns before copying.
    /// </summary>
    public partial class AddStudyItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReviewLogs_Vocabulary_VocabularyId",
                table: "ReviewLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_SrsItems_Vocabulary_VocabularyId",
                table: "SrsItems");

            migrationBuilder.CreateTable(
                name: "StudyItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsAsleep = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyItems", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Copy every vocabulary item across, keeping its id (Kind 0 = Vocabulary).
            migrationBuilder.Sql(
                "INSERT INTO `StudyItems` (`Id`, `Kind`, `IsAsleep`, `CreatedAt`) " +
                "SELECT `Id`, 0, `IsAsleep`, `CreatedAt` FROM `Vocabulary`;");

            migrationBuilder.CreateIndex(
                name: "IX_StudyItems_IsAsleep",
                table: "StudyItems",
                column: "IsAsleep");

            migrationBuilder.CreateIndex(
                name: "IX_StudyItems_Kind",
                table: "StudyItems",
                column: "Kind");

            migrationBuilder.DropIndex(
                name: "IX_Vocabulary_IsAsleep",
                table: "Vocabulary");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Vocabulary");

            migrationBuilder.DropColumn(
                name: "IsAsleep",
                table: "Vocabulary");

            migrationBuilder.RenameColumn(
                name: "VocabularyId",
                table: "SrsItems",
                newName: "StudyItemId");

            migrationBuilder.RenameIndex(
                name: "IX_SrsItems_VocabularyId",
                table: "SrsItems",
                newName: "IX_SrsItems_StudyItemId");

            migrationBuilder.RenameColumn(
                name: "VocabularyId",
                table: "ReviewLogs",
                newName: "StudyItemId");

            migrationBuilder.RenameIndex(
                name: "IX_ReviewLogs_VocabularyId",
                table: "ReviewLogs",
                newName: "IX_ReviewLogs_StudyItemId");

            // Vocabulary ids now come from StudyItems, so drop the auto-increment.
            // MySQL refuses to modify a column other tables' foreign keys point at.
            DropVocabularyChildForeignKeys(migrationBuilder);
            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Vocabulary",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);
            AddVocabularyChildForeignKeys(migrationBuilder);

            migrationBuilder.AddForeignKey(
                name: "FK_ReviewLogs_StudyItems_StudyItemId",
                table: "ReviewLogs",
                column: "StudyItemId",
                principalTable: "StudyItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SrsItems_StudyItems_StudyItemId",
                table: "SrsItems",
                column: "StudyItemId",
                principalTable: "StudyItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Vocabulary_StudyItems_Id",
                table: "Vocabulary",
                column: "Id",
                principalTable: "StudyItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReviewLogs_StudyItems_StudyItemId",
                table: "ReviewLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_SrsItems_StudyItems_StudyItemId",
                table: "SrsItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Vocabulary_StudyItems_Id",
                table: "Vocabulary");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Vocabulary",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsAsleep",
                table: "Vocabulary",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Copy the shared columns back before StudyItems goes away. Only
            // vocabulary existed before this migration; other kinds are lost.
            migrationBuilder.Sql(
                "UPDATE `Vocabulary` v JOIN `StudyItems` s ON s.`Id` = v.`Id` " +
                "SET v.`IsAsleep` = s.`IsAsleep`, v.`CreatedAt` = s.`CreatedAt`;");

            migrationBuilder.Sql(
                "DELETE FROM `SrsItems` WHERE `StudyItemId` NOT IN (SELECT `Id` FROM `Vocabulary`);");

            migrationBuilder.Sql(
                "DELETE FROM `ReviewLogs` WHERE `StudyItemId` NOT IN (SELECT `Id` FROM `Vocabulary`);");

            migrationBuilder.DropTable(
                name: "StudyItems");

            migrationBuilder.CreateIndex(
                name: "IX_Vocabulary_IsAsleep",
                table: "Vocabulary",
                column: "IsAsleep");

            migrationBuilder.RenameColumn(
                name: "StudyItemId",
                table: "SrsItems",
                newName: "VocabularyId");

            migrationBuilder.RenameIndex(
                name: "IX_SrsItems_StudyItemId",
                table: "SrsItems",
                newName: "IX_SrsItems_VocabularyId");

            migrationBuilder.RenameColumn(
                name: "StudyItemId",
                table: "ReviewLogs",
                newName: "VocabularyId");

            migrationBuilder.RenameIndex(
                name: "IX_ReviewLogs_StudyItemId",
                table: "ReviewLogs",
                newName: "IX_ReviewLogs_VocabularyId");

            DropVocabularyChildForeignKeys(migrationBuilder);
            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Vocabulary",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);
            AddVocabularyChildForeignKeys(migrationBuilder);

            migrationBuilder.AddForeignKey(
                name: "FK_ReviewLogs_Vocabulary_VocabularyId",
                table: "ReviewLogs",
                column: "VocabularyId",
                principalTable: "Vocabulary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SrsItems_Vocabulary_VocabularyId",
                table: "SrsItems",
                column: "VocabularyId",
                principalTable: "Vocabulary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <summary>Tables whose VocabularyId column references Vocabulary.Id.</summary>
        private static readonly string[] VocabularyChildTables =
        {
            "Conjugations",
            "VocabularyAudios",
            "VocabularyMeanings",
        };

        private static void DropVocabularyChildForeignKeys(MigrationBuilder migrationBuilder)
        {
            foreach (string table in VocabularyChildTables)
            {
                migrationBuilder.DropForeignKey(
                    name: $"FK_{table}_Vocabulary_VocabularyId",
                    table: table);
            }
        }

        private static void AddVocabularyChildForeignKeys(MigrationBuilder migrationBuilder)
        {
            foreach (string table in VocabularyChildTables)
            {
                migrationBuilder.AddForeignKey(
                    name: $"FK_{table}_Vocabulary_VocabularyId",
                    table: table,
                    column: "VocabularyId",
                    principalTable: "Vocabulary",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }
    }
}
