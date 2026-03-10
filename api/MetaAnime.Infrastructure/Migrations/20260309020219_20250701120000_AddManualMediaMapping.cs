using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaAnime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20250701120000_AddManualMediaMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManualMediaMapping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlexSourceId = table.Column<string>(type: "text", nullable: false),
                    PlexTitle = table.Column<string>(type: "text", nullable: false),
                    MalId = table.Column<string>(type: "text", nullable: false),
                    MalTitle = table.Column<string>(type: "text", nullable: false),
                    AniDbId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualMediaMapping", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualMediaMapping_PlexSourceId",
                table: "ManualMediaMapping",
                column: "PlexSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualMediaMapping_PlexSourceId_MalId",
                table: "ManualMediaMapping",
                columns: new[] { "PlexSourceId", "MalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManualMediaMapping");
        }
    }
}
