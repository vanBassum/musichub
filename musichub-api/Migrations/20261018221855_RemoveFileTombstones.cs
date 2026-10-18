using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFileTombstones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tombstones from the earlier deletion design: their object was already confirmed removed, and the
            // Deleted status no longer exists, so the rows must go before the enum can stop reading them.
            migrationBuilder.Sql("DELETE FROM \"Files\" WHERE \"Status\" = 'Deleted';");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Files");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the schema comes back: the Deleted rows removed by Up() are not restored, and DeletedAt
            // returns empty. Rolling back cannot recover the deleted history.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Files",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
