using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadRequestRemovedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedAt",
                table: "DownloadRequests",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "DownloadRequests");
        }
    }
}
