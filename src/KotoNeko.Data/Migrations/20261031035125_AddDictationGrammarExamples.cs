using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDictationGrammarExamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GrammarExampleId",
                table: "DictationAttempts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DictationAttempts_GrammarExampleId",
                table: "DictationAttempts",
                column: "GrammarExampleId");

            migrationBuilder.AddForeignKey(
                name: "FK_DictationAttempts_GrammarExamples_GrammarExampleId",
                table: "DictationAttempts",
                column: "GrammarExampleId",
                principalTable: "GrammarExamples",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DictationAttempts_GrammarExamples_GrammarExampleId",
                table: "DictationAttempts");

            migrationBuilder.DropIndex(
                name: "IX_DictationAttempts_GrammarExampleId",
                table: "DictationAttempts");

            migrationBuilder.DropColumn(
                name: "GrammarExampleId",
                table: "DictationAttempts");
        }
    }
}
