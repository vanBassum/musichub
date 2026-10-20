using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadHistory : Migration
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

            migrationBuilder.CreateTable(
                name: "DownloadHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrackId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    BitrateKbps = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FileSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AudioSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AudioHashAlgorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FileId = table.Column<int>(type: "integer", nullable: true),
                    JobExecutionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadHistories_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DownloadHistories_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationOutcomes_DownloadId",
                table: "IdentificationOutcomes",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadHistories_AudioSha256",
                table: "DownloadHistories",
                column: "AudioSha256");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadHistories_FileId",
                table: "DownloadHistories",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadHistories_FileSha256",
                table: "DownloadHistories",
                column: "FileSha256");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadHistories_JobExecutionId",
                table: "DownloadHistories",
                column: "JobExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadHistories_TrackId_Provider_Source",
                table: "DownloadHistories",
                columns: new[] { "TrackId", "Provider", "Source" });

            migrationBuilder.AddForeignKey(
                name: "FK_IdentificationOutcomes_DownloadHistories_DownloadId",
                table: "IdentificationOutcomes",
                column: "DownloadId",
                principalTable: "DownloadHistories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IdentificationOutcomes_DownloadHistories_DownloadId",
                table: "IdentificationOutcomes");

            migrationBuilder.DropTable(
                name: "DownloadHistories");

            migrationBuilder.DropIndex(
                name: "IX_IdentificationOutcomes_DownloadId",
                table: "IdentificationOutcomes");

            migrationBuilder.DropColumn(
                name: "DownloadId",
                table: "IdentificationOutcomes");

            migrationBuilder.DropColumn(
                name: "PipelineVersion",
                table: "IdentificationOutcomes");

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
