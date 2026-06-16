using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVocabularyMeanings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the new meanings table first.
            migrationBuilder.CreateTable(
                name: "VocabularyMeanings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VocabularyId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyMeanings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VocabularyMeanings_Vocabulary_VocabularyId",
                        column: x => x.VocabularyId,
                        principalTable: "Vocabulary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyMeanings_VocabularyId_SortOrder",
                table: "VocabularyMeanings",
                columns: new[] { "VocabularyId", "SortOrder" });

            // 2. Preserve existing data: split the old Meaning (+ AlternateMeanings)
            //    on ';' into individual rows. This also repairs entries where jisho
            //    had concatenated multiple meanings into a single field.
            migrationBuilder.Sql(@"
INSERT INTO `VocabularyMeanings` (`VocabularyId`, `Text`, `SortOrder`)
WITH RECURSIVE `src` AS (
    SELECT `Id` AS `VocabularyId`,
           CONCAT(
               COALESCE(`Meaning`, ''),
               CASE WHEN COALESCE(`AlternateMeanings`, '') <> ''
                    THEN CONCAT(';', `AlternateMeanings`)
                    ELSE '' END
           ) AS `remaining`,
           0 AS `ord`
    FROM `Vocabulary`
),
`split` AS (
    SELECT `VocabularyId`,
           TRIM(SUBSTRING_INDEX(`remaining`, ';', 1)) AS `part`,
           CASE WHEN LOCATE(';', `remaining`) > 0
                THEN SUBSTRING(`remaining`, LOCATE(';', `remaining`) + 1)
                ELSE NULL END AS `rest`,
           `ord`
    FROM `src`
    UNION ALL
    SELECT `VocabularyId`,
           TRIM(SUBSTRING_INDEX(`rest`, ';', 1)),
           CASE WHEN LOCATE(';', `rest`) > 0
                THEN SUBSTRING(`rest`, LOCATE(';', `rest`) + 1)
                ELSE NULL END,
           `ord` + 1
    FROM `split`
    WHERE `rest` IS NOT NULL
)
SELECT `VocabularyId`, `part`, `ord`
FROM `split`
WHERE `part` <> '';
");

            // 3. Drop the now-migrated columns.
            migrationBuilder.DropColumn(
                name: "AlternateMeanings",
                table: "Vocabulary");

            migrationBuilder.DropColumn(
                name: "Meaning",
                table: "Vocabulary");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Recreate the old columns.
            migrationBuilder.AddColumn<string>(
                name: "AlternateMeanings",
                table: "Vocabulary",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Meaning",
                table: "Vocabulary",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            // 2. Best-effort: fold the meaning rows back into the single column.
            migrationBuilder.Sql(@"
UPDATE `Vocabulary` v
SET `Meaning` = COALESCE((
    SELECT GROUP_CONCAT(m.`Text` ORDER BY m.`SortOrder` SEPARATOR '; ')
    FROM `VocabularyMeanings` m
    WHERE m.`VocabularyId` = v.`Id`
), '');
");

            // 3. Drop the meanings table.
            migrationBuilder.DropTable(
                name: "VocabularyMeanings");
        }
    }
}
