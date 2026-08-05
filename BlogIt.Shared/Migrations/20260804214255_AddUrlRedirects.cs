using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogIt.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlRedirects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UrlRedirects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TargetUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsPermanent = table.Column<bool>(type: "bit", nullable: false),
                    IsAutomatic = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UrlRedirects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UrlRedirects_SourcePath",
                table: "UrlRedirects",
                column: "SourcePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UrlRedirects");
        }
    }
}
