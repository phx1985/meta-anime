using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaAnime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AnimeMapping_AniDbId",
                table: "AnimeMapping",
                column: "AniDbId");

            migrationBuilder.CreateIndex(
                name: "IX_Anime_AnimeSource",
                table: "Anime",
                column: "AnimeSource");

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntry_SourceId",
                table: "CacheEntry",
                column: "SourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnimeMapping_AniDbId",
                table: "AnimeMapping");

            migrationBuilder.DropIndex(
                name: "IX_Anime_AnimeSource",
                table: "Anime");

            migrationBuilder.DropIndex(
                name: "IX_CacheEntry_SourceId",
                table: "CacheEntry");
        }
    }
}
