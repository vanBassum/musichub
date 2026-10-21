using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrackId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RemotePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    BitrateKbps = table.Column<int>(type: "integer", nullable: true),
                    Format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    JobExecutionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    BytesTransferred = table.Column<long>(type: "bigint", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Failure = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AcquisitionDownloadId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadRequests_AcquisitionDownloads_AcquisitionDownloadId",
                        column: x => x.AcquisitionDownloadId,
                        principalTable: "AcquisitionDownloads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DownloadRequests_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_AcquisitionDownloadId",
                table: "DownloadRequests",
                column: "AcquisitionDownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_Provider_Source_RemotePath",
                table: "DownloadRequests",
                columns: new[] { "Provider", "Source", "RemotePath" },
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Downloading', 'Downloaded')");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_Status",
                table: "DownloadRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_TrackId",
                table: "DownloadRequests",
                column: "TrackId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Downloading', 'Downloaded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadRequests");
        }
    }
}
