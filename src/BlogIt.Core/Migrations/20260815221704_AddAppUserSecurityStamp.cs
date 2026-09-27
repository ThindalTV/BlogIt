using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogIt.Migrations
{
    /// <inheritdoc />
    public partial class AddAppUserSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // The scaffolded "" default cannot be left in place: authentication rejects a token
            // whose stamp claim is empty, and the entity's own GUID initializer only runs for
            // rows created in C#, never for rows already in the table. Every existing account
            // would authenticate with an empty stamp and be locked out permanently on upgrade.
            // NEWID() is SQL Server syntax, which is the only relational family this package
            // ships a provider for (UseSqlServer / UseAzureSql).
            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET [SecurityStamp] = REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')
                WHERE [SecurityStamp] = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");
        }
    }
}
