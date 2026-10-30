using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ItsTool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketTimeMeasures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketTimeMeasures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    OwnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OwnedByUserId = table.Column<int>(type: "int", nullable: true),
                    TimeToOwnMinutes = table.Column<int>(type: "int", nullable: true),
                    WaitingMinutes = table.Column<int>(type: "int", nullable: false),
                    WaitingSince = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimeToResolveMinutes = table.Column<int>(type: "int", nullable: true),
                    ReopenCount = table.Column<int>(type: "int", nullable: false),
                    AssignmentCount = table.Column<int>(type: "int", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketTimeMeasures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketTimeMeasures_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketTimeMeasures_ResolvedAt",
                table: "TicketTimeMeasures",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TicketTimeMeasures_TicketId",
                table: "TicketTimeMeasures",
                column: "TicketId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketTimeMeasures");
        }
    }
}
