using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackActivities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrackId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SearchId = table.Column<Guid>(type: "uuid", nullable: true),
                    DownloadRequestId = table.Column<int>(type: "integer", nullable: true),
                    AcquisitionDownloadId = table.Column<int>(type: "integer", nullable: true),
                    JobExecutionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: true),
                    ResultCount = table.Column<int>(type: "integer", nullable: true),
                    PlausibleCount = table.Column<int>(type: "integer", nullable: true),
                    EligibleCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackActivities_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackActivities_DownloadRequestId",
                table: "TrackActivities",
                column: "DownloadRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackActivities_SearchId",
                table: "TrackActivities",
                column: "SearchId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackActivities_TrackId_Id",
                table: "TrackActivities",
                columns: new[] { "TrackId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackActivities");
        }
    }
}
