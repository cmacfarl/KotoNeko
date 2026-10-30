using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmCallFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Feature",
                table: "LlmCalls",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Calls already logged get their feature from the prompt that made them. Grading
            // calls all count as the eval: lesson grading shares the prompt and came later.
            migrationBuilder.Sql("""
                UPDATE `LlmCalls` SET `Feature` = CASE
                    WHEN `PromptVersion` LIKE 'practice-sentences-%' THEN 1
                    WHEN `PromptVersion` LIKE 'grammar-content-%' THEN 2
                    WHEN `PromptVersion` LIKE 'grade-%' THEN 4
                    WHEN `PromptVersion` LIKE 'prelabel-%' THEN 5
                    ELSE 0 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Feature",
                table: "LlmCalls");
        }
    }
}
