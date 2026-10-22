using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogIt.Sqlite.Migrations
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
                type: "TEXT",
                maxLength: 200,
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "MediaFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_Slug",
                table: "MediaFiles",
                column: "Slug",
                unique: true);
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
