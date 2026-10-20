using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AcquisitionDownloadsForMissingMusic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "FileId",
                table: "IdentificationOutcomes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "DownloadId",
                table: "IdentificationOutcomes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PipelineVersion",
                table: "IdentificationOutcomes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BitrateKbps",
                table: "AcquisitionDownloads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobExecutionId",
                table: "AcquisitionDownloads",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "AcquisitionDownloads",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationOutcomes_DownloadId",
                table: "IdentificationOutcomes",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionDownloads_JobExecutionId",
                table: "AcquisitionDownloads",
                column: "JobExecutionId");

            migrationBuilder.AddForeignKey(
                name: "FK_IdentificationOutcomes_AcquisitionDownloads_DownloadId",
                table: "IdentificationOutcomes",
                column: "DownloadId",
                principalTable: "AcquisitionDownloads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IdentificationOutcomes_AcquisitionDownloads_DownloadId",
                table: "IdentificationOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_IdentificationOutcomes_DownloadId",
                table: "IdentificationOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_AcquisitionDownloads_JobExecutionId",
                table: "AcquisitionDownloads");

            migrationBuilder.DropColumn(
                name: "DownloadId",
                table: "IdentificationOutcomes");

            migrationBuilder.DropColumn(
                name: "PipelineVersion",
                table: "IdentificationOutcomes");

            migrationBuilder.DropColumn(
                name: "BitrateKbps",
                table: "AcquisitionDownloads");

            migrationBuilder.DropColumn(
                name: "JobExecutionId",
                table: "AcquisitionDownloads");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "AcquisitionDownloads");

            migrationBuilder.AlterColumn<int>(
                name: "FileId",
                table: "IdentificationOutcomes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
