using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaAnime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Anime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AnimeSource = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anime", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CacheEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "text", nullable: false),
                    RawData = table.Column<string>(type: "jsonb", nullable: false),
                    AnimeSource = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KometaAnimeMapping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AniDbId = table.Column<string>(type: "text", nullable: false),
                    PlatformName = table.Column<string>(type: "text", nullable: false),
                    PlatformId = table.Column<string>(type: "text", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: true),
                    EpOffset = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KometaAnimeMapping", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnimeMapping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AniDbId = table.Column<string>(type: "text", nullable: false),
                    AnimeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimeMapping", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnimeMapping_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalAnimeIdentifier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaPlatform = table.Column<int>(type: "integer", nullable: false),
                    MediaPlatformId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAnimeIdentifier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalAnimeIdentifier_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnimeMapping_AnimeId",
                table: "AnimeMapping",
                column: "AnimeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAnimeIdentifier_AnimeId",
                table: "ExternalAnimeIdentifier",
                column: "AnimeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnimeMapping");

            migrationBuilder.DropTable(
                name: "CacheEntry");

            migrationBuilder.DropTable(
                name: "ExternalAnimeIdentifier");

            migrationBuilder.DropTable(
                name: "KometaAnimeMapping");

            migrationBuilder.DropTable(
                name: "Anime");
        }
    }
}
