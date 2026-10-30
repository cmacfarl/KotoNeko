using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGradingPrelabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LabelSource",
                table: "GradingTestItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsPerson",
                table: "GradingTestItems",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "AiAgreedAgreement",
                table: "GradingEvalRuns",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "AiAgreedGraded",
                table: "GradingEvalRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Effort",
                table: "GradingEvalRuns",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "GraderVoted",
                table: "GradingEvalResults",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LabelSource",
                table: "GradingEvalResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GradingPrelabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Effort = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PromptVersion = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Label = table.Column<int>(type: "int", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingPrelabels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradingPrelabels_GradingTestItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "GradingTestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GradingPrelabels_ItemId_Provider_Model_PromptVersion",
                table: "GradingPrelabels",
                columns: new[] { "ItemId", "Provider", "Model", "PromptVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradingPrelabels");

            migrationBuilder.DropColumn(
                name: "LabelSource",
                table: "GradingTestItems");

            migrationBuilder.DropColumn(
                name: "NeedsPerson",
                table: "GradingTestItems");

            migrationBuilder.DropColumn(
                name: "AiAgreedAgreement",
                table: "GradingEvalRuns");

            migrationBuilder.DropColumn(
                name: "AiAgreedGraded",
                table: "GradingEvalRuns");

            migrationBuilder.DropColumn(
                name: "Effort",
                table: "GradingEvalRuns");

            migrationBuilder.DropColumn(
                name: "GraderVoted",
                table: "GradingEvalResults");

            migrationBuilder.DropColumn(
                name: "LabelSource",
                table: "GradingEvalResults");
        }
    }
}
