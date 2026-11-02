using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ItsTool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeToClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "TicketTimeMeasures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeToCloseMinutes",
                table: "TicketTimeMeasures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFinal",
                table: "Statuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The last closed status (Closed, after Resolved) is the final one
            migrationBuilder.Sql(@"
UPDATE Statuses SET IsFinal = 1
WHERE IsDeleted = 0 AND IsClosedStatus = 1
  AND SortOrder = (SELECT MAX(SortOrder) FROM Statuses WHERE IsDeleted = 0 AND IsClosedStatus = 1);");

            // The measures are worked out from the ticket history; the startup backfill
            // (TicketTimeBackfillService) calculates them again, now with the time to close.
            migrationBuilder.Sql("DELETE FROM TicketTimeMeasures;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "TicketTimeMeasures");

            migrationBuilder.DropColumn(
                name: "TimeToCloseMinutes",
                table: "TicketTimeMeasures");

            migrationBuilder.DropColumn(
                name: "IsFinal",
                table: "Statuses");
        }
    }
}
