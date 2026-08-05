using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogIt.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicationSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasBeenPublished",
                table: "Pages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledPublishAt",
                table: "Pages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasBeenPublished",
                table: "BlogPosts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledUnpublishAt",
                table: "Pages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledPublishAt",
                table: "BlogPosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledUnpublishAt",
                table: "BlogPosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE Pages SET HasBeenPublished = 1 WHERE IsPublished = 1");
            migrationBuilder.Sql(
                "UPDATE BlogPosts SET HasBeenPublished = 1 WHERE IsPublished = 1 OR PublishedAt IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_ScheduledPublishAt",
                table: "Pages",
                column: "ScheduledPublishAt");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_ScheduledUnpublishAt",
                table: "Pages",
                column: "ScheduledUnpublishAt");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_ScheduledPublishAt",
                table: "BlogPosts",
                column: "ScheduledPublishAt");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_ScheduledUnpublishAt",
                table: "BlogPosts",
                column: "ScheduledUnpublishAt");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasBeenPublished",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Pages_ScheduledPublishAt",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Pages_ScheduledUnpublishAt",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_BlogPosts_ScheduledPublishAt",
                table: "BlogPosts");

            migrationBuilder.DropIndex(
                name: "IX_BlogPosts_ScheduledUnpublishAt",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "ScheduledPublishAt",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ScheduledUnpublishAt",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ScheduledPublishAt",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "ScheduledUnpublishAt",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "HasBeenPublished",
                table: "BlogPosts");
        }
    }
}
