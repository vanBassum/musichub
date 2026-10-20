using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionAudioHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioHash",
                table: "AcquisitionDownloads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioHashStatus",
                table: "AcquisitionDownloads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioHashVersion",
                table: "AcquisitionDownloads",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionDownloads_AudioHash",
                table: "AcquisitionDownloads",
                column: "AudioHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcquisitionDownloads_AudioHash",
                table: "AcquisitionDownloads");

            migrationBuilder.DropColumn(
                name: "AudioHash",
                table: "AcquisitionDownloads");

            migrationBuilder.DropColumn(
                name: "AudioHashStatus",
                table: "AcquisitionDownloads");

            migrationBuilder.DropColumn(
                name: "AudioHashVersion",
                table: "AcquisitionDownloads");
        }
    }
}
