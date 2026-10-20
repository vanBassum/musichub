using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppliedIdentifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppliedIdentifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileId = table.Column<int>(type: "integer", nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AttemptId = table.Column<int>(type: "integer", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppliedIdentifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppliedIdentifications_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppliedIdentifications_IdentificationOutcomes_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "IdentificationOutcomes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppliedIdentifications_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppliedIdentifications_AttemptId",
                table: "AppliedIdentifications",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_AppliedIdentifications_FileId_AppliedAt",
                table: "AppliedIdentifications",
                columns: new[] { "FileId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppliedIdentifications_TrackId",
                table: "AppliedIdentifications",
                column: "TrackId");

            // Existing links keep their provenance: every recorded Identified attempt becomes an automatic decision.
            migrationBuilder.Sql("""
                INSERT INTO "AppliedIdentifications" ("FileId", "TrackId", "Source", "AttemptId", "AppliedAt")
                SELECT o."FileId", t."Id", 'Automatic', o."Id", o."RecordedAt"
                FROM "IdentificationOutcomes" o
                LEFT JOIN "Tracks" t ON t."Id" = o."TrackId"
                WHERE o."Decision" = 'Identified'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppliedIdentifications");
        }
    }
}
