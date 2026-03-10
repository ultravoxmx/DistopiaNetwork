using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistopiaNetwork.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Podcasts",
                columns: table => new
                {
                    PodcastId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    PublisherPubKey = table.Column<string>(type: "nvarchar(MAX)", nullable: false),
                    PublisherServer = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4096)", maxLength: 4096, nullable: false, defaultValue: ""),
                    ImageUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    PublishTimestamp = table.Column<long>(type: "bigint", nullable: false),
                    Signature = table.Column<string>(type: "nvarchar(MAX)", nullable: false),
                    InsertedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Podcasts", x => x.PodcastId);
                });

            migrationBuilder.CreateTable(
                name: "CacheEntries",
                columns: table => new
                {
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    LastAccess = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryTimestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PodcastId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheEntries", x => x.FileHash);
                    table.ForeignKey(
                        name: "FK_CacheEntries_Podcasts_PodcastId",
                        column: x => x.PodcastId,
                        principalTable: "Podcasts",
                        principalColumn: "PodcastId",
                        onDelete: ReferentialAction.SetNull);
                });

            // Indici per Podcasts
            migrationBuilder.CreateIndex(
                name: "IX_Podcasts_FileHash",
                table: "Podcasts",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_Podcasts_PublishTimestamp",
                table: "Podcasts",
                column: "PublishTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Podcasts_PublisherServer",
                table: "Podcasts",
                column: "PublisherServer");

            // Indice per CacheEntries
            migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_Expiry",
                table: "CacheEntries",
                column: "ExpiryTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_PodcastId",
                table: "CacheEntries",
                column: "PodcastId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CacheEntries");
            migrationBuilder.DropTable(name: "Podcasts");
        }
    }
}
