using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KotoNeko.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVocabularyAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasSentenceAudio",
                table: "Vocabulary",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasWordAudio",
                table: "Vocabulary",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PlaySentenceAudioInReview",
                table: "Vocabulary",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "VocabularyAudios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VocabularyId = table.Column<int>(type: "int", nullable: false),
                    WordAudio = table.Column<byte[]>(type: "longblob", nullable: false),
                    SentenceAudio = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyAudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VocabularyAudios_Vocabulary_VocabularyId",
                        column: x => x.VocabularyId,
                        principalTable: "Vocabulary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyAudios_VocabularyId",
                table: "VocabularyAudios",
                column: "VocabularyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VocabularyAudios");

            migrationBuilder.DropColumn(
                name: "HasSentenceAudio",
                table: "Vocabulary");

            migrationBuilder.DropColumn(
                name: "HasWordAudio",
                table: "Vocabulary");

            migrationBuilder.DropColumn(
                name: "PlaySentenceAudioInReview",
                table: "Vocabulary");
        }
    }
}
