using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadHandoffProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DownloadRequests_Provider_Source_RemotePath",
                table: "DownloadRequests");

            migrationBuilder.DropIndex(
                name: "IX_DownloadRequests_TrackId",
                table: "DownloadRequests");

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                table: "DownloadRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_Provider_Source_RemotePath",
                table: "DownloadRequests",
                columns: new[] { "Provider", "Source", "RemotePath" },
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Downloading', 'Downloaded', 'Processing')");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_TrackId",
                table: "DownloadRequests",
                column: "TrackId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Downloading', 'Downloaded', 'Processing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DownloadRequests_Provider_Source_RemotePath",
                table: "DownloadRequests");

            migrationBuilder.DropIndex(
                name: "IX_DownloadRequests_TrackId",
                table: "DownloadRequests");

            migrationBuilder.DropColumn(
                name: "ProcessingAttempts",
                table: "DownloadRequests");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_Provider_Source_RemotePath",
                table: "DownloadRequests",
                columns: new[] { "Provider", "Source", "RemotePath" },
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Downloading', 'Downloaded')");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_TrackId",
                table: "DownloadRequests",
                column: "TrackId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Downloading', 'Downloaded')");
        }
    }
}
