using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogIt.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFileSlugAndUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "MediaFiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "MediaFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_Slug",
                table: "MediaFiles",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_Slug",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "MediaFiles");
        }
    }
}
