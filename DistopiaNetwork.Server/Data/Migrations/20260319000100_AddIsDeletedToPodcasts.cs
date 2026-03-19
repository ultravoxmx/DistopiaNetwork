using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistopiaNetwork.Server.Data.Migrations
{
    public partial class AddIsDeletedToPodcasts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Podcasts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Podcasts_IsDeleted",
                table: "Podcasts",
                column: "IsDeleted");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Podcasts_IsDeleted",
                table: "Podcasts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Podcasts");
        }
    }
}
