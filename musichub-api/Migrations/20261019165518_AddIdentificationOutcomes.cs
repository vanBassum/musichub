using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentificationOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentificationOutcomes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobExecutionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: true),
                    MethodsJson = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentificationOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentificationOutcomes_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationOutcomes_FileId",
                table: "IdentificationOutcomes",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationOutcomes_JobExecutionId_FileId",
                table: "IdentificationOutcomes",
                columns: new[] { "JobExecutionId", "FileId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationOutcomes_RecordedAt",
                table: "IdentificationOutcomes",
                column: "RecordedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentificationOutcomes");
        }
    }
}
