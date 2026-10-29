using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DailyMinutesTarget = table.Column<int>(type: "int", nullable: false),
                    DailyLessonCap = table.Column<int>(type: "int", nullable: false),
                    LessonBatchSize = table.Column<int>(type: "int", nullable: false),
                    ListeningEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ListeningCountsTowardSrs = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SpeechSpeed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudySettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "StudySettings",
                columns: new[] { "Id", "DailyLessonCap", "DailyMinutesTarget", "LessonBatchSize", "ListeningCountsTowardSrs", "ListeningEnabled", "SpeechSpeed" },
                values: new object[] { 1, 15, 60, 5, false, true, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudySettings");
        }
    }
}
