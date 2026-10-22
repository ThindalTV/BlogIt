using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogIt.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFileViewCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ViewCount",
                table: "MediaFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ViewCount",
                table: "MediaFiles");
        }
    }
}
