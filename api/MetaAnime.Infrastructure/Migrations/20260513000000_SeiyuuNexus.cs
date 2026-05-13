using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaAnime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeiyuuNexus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnimeCast",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimeMalId = table.Column<string>(type: "text", nullable: false),
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    CharacterName = table.Column<string>(type: "text", nullable: false),
                    CharacterRole = table.Column<string>(type: "text", nullable: false),
                    CharacterImageUrl = table.Column<string>(type: "text", nullable: true),
                    VaPersonId = table.Column<int>(type: "integer", nullable: false),
                    VaName = table.Column<string>(type: "text", nullable: false),
                    VaImageUrl = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimeCast", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VoiceActors",
                columns: table => new
                {
                    PersonId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FamilyName = table.Column<string>(type: "text", nullable: true),
                    GivenName = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Birthday = table.Column<string>(type: "text", nullable: true),
                    BloodType = table.Column<string>(type: "text", nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    WeightKg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    CachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceActors", x => x.PersonId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnimeCast_AnimeMalId",
                table: "AnimeCast",
                column: "AnimeMalId");

            migrationBuilder.CreateIndex(
                name: "IX_AnimeCast_VaPersonId",
                table: "AnimeCast",
                column: "VaPersonId");

            migrationBuilder.CreateIndex(
                name: "UX_AnimeCast_Anime_Character_Language",
                table: "AnimeCast",
                columns: new[] { "AnimeMalId", "CharacterId", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AnimeCast");
            migrationBuilder.DropTable(name: "VoiceActors");
        }
    }
}
