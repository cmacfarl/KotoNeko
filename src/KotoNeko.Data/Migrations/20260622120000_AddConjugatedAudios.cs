using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConjugatedAudios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasAudio",
                table: "Conjugations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConjugatedAudios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ConjugationId = table.Column<int>(type: "int", nullable: false),
                    Audio = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConjugatedAudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConjugatedAudios_Conjugations_ConjugationId",
                        column: x => x.ConjugationId,
                        principalTable: "Conjugations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ConjugatedAudios_ConjugationId",
                table: "ConjugatedAudios",
                column: "ConjugationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConjugatedAudios");

            migrationBuilder.DropColumn(
                name: "HasAudio",
                table: "Conjugations");
        }
    }
}
