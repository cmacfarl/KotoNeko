using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelAssignments",
                columns: table => new
                {
                    Feature = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelAssignments", x => x.Feature);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // The model chosen in Settings for listening scripts ("Provider|model") wrote casual
            // versions too: it becomes the choice for both (ListeningScript 10, CasualRewrite 12).
            migrationBuilder.Sql(@"
INSERT INTO ModelAssignments (Feature, Provider, Model, ChangedAt)
SELECT f.Feature, SUBSTRING_INDEX(s.ListeningScriptModel, '|', 1),
       SUBSTRING(s.ListeningScriptModel, LOCATE('|', s.ListeningScriptModel) + 1), UTC_TIMESTAMP(6)
FROM StudySettings s CROSS JOIN (SELECT 10 AS Feature UNION ALL SELECT 12) f
WHERE s.Id = 1 AND LOCATE('|', s.ListeningScriptModel) > 1
  AND LOCATE('|', s.ListeningScriptModel) < CHAR_LENGTH(s.ListeningScriptModel);");

            migrationBuilder.DropColumn(
                name: "ListeningScriptModel",
                table: "StudySettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ListeningScriptModel",
                table: "StudySettings",
                type: "varchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StudySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ListeningScriptModel",
                value: "");

            // Only the listening-script choice has somewhere to go back to.
            migrationBuilder.Sql(@"
UPDATE StudySettings s JOIN ModelAssignments a ON a.Feature = 10
SET s.ListeningScriptModel = LEFT(CONCAT(a.Provider, '|', a.Model), 120)
WHERE s.Id = 1;");

            migrationBuilder.DropTable(
                name: "ModelAssignments");
        }
    }
}
