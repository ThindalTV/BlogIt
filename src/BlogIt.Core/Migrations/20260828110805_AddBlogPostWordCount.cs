using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogIt.Migrations
{
    /// <inheritdoc />
    public partial class AddBlogPostWordCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WordCount",
                table: "BlogPosts",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WordCount",
                table: "BlogPosts");
        }
    }
}
