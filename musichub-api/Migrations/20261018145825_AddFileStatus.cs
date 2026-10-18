using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFileStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "Files",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Files",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Existing rows: a hash was only written after storage confirmed success.
            migrationBuilder.Sql(
                "UPDATE \"Files\" SET \"Status\" = CASE WHEN \"ContentHash\" IS NOT NULL THEN 'Stored' ELSE 'Pending' END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Files");
        }
    }
}
