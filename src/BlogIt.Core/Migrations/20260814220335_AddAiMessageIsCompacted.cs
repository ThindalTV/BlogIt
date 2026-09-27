using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogIt.Migrations
{
    /// <inheritdoc />
    public partial class AddAiMessageIsCompacted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCompacted",
                table: "AiMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCompacted",
                table: "AiMessages");
        }
    }
}
