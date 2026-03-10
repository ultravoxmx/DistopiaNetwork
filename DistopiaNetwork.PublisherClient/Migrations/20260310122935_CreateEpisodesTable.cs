using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistopiaNetwork.PublisherClient.Migrations
{
    /// <inheritdoc />
    public partial class CreateEpisodesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    PodcastId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LocalFilePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishTimestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "Draft"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.PodcastId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_CreatedAt",
                table: "Episodes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_FileHash",
                table: "Episodes",
                column: "FileHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_UploadStatus",
                table: "Episodes",
                column: "UploadStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Episodes");
        }
    }
}
