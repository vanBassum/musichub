using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcquisitionDownloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RemotePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IntendedTrackId = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Failure = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionDownloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcquisitionDownloads_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AcquisitionDownloads_Tracks_IntendedTrackId",
                        column: x => x.IntendedTrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionDownloads_ContentHash",
                table: "AcquisitionDownloads",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionDownloads_FileId",
                table: "AcquisitionDownloads",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionDownloads_IntendedTrackId",
                table: "AcquisitionDownloads",
                column: "IntendedTrackId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionDownloads_Provider_Source_RemotePath",
                table: "AcquisitionDownloads",
                columns: new[] { "Provider", "Source", "RemotePath" });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionDownloads_StartedAt",
                table: "AcquisitionDownloads",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcquisitionDownloads");
        }
    }
}
