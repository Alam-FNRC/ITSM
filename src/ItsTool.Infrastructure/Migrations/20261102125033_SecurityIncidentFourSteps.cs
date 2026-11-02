using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ItsTool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecurityIncidentFourSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Containment, Eradication, Recovery and Review (2-5) become Responding (2); Closed moves from 6 to 3
            migrationBuilder.Sql("UPDATE SecurityIncidents SET Status = CASE WHEN Status BETWEEN 2 AND 5 THEN 2 WHEN Status = 6 THEN 3 ELSE Status END;");

            migrationBuilder.DropColumn(
                name: "ContainedAt",
                table: "SecurityIncidents");

            migrationBuilder.DropColumn(
                name: "EradicatedAt",
                table: "SecurityIncidents");

            migrationBuilder.DropColumn(
                name: "RecoveredAt",
                table: "SecurityIncidents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContainedAt",
                table: "SecurityIncidents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EradicatedAt",
                table: "SecurityIncidents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveredAt",
                table: "SecurityIncidents",
                type: "datetime2",
                nullable: true);

            // Responding goes back to Containment; Closed back to 6
            migrationBuilder.Sql("UPDATE SecurityIncidents SET Status = CASE WHEN Status = 3 THEN 6 ELSE Status END;");
        }
    }
}
