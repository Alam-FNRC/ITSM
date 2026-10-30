using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ItsTool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecurityIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReporterUserId = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HandlerUserId = table.Column<int>(type: "int", nullable: true),
                    TriageOutcome = table.Column<int>(type: "int", nullable: false),
                    TriageReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TriagedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TriagedByUserId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: true),
                    PersonalDataInvolved = table.Column<bool>(type: "bit", nullable: false),
                    ContainedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EradicatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecoveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewRootCause = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewWhatWorked = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewImprovements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewFollowUps = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewCompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewCompletedByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_SecurityIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAffectedItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusChangedByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_SecurityAffectedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityAffectedItems_SecurityIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "SecurityIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityIncidentAccesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_SecurityIncidentAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityIncidentAccesses_SecurityIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "SecurityIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityIncidentActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_SecurityIncidentActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityIncidentActions_SecurityIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "SecurityIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityIncidentMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    AddedByUserId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_SecurityIncidentMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityIncidentMembers_SecurityIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "SecurityIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAffectedItems_IncidentId",
                table: "SecurityAffectedItems",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidentAccesses_IncidentId",
                table: "SecurityIncidentAccesses",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidentActions_IncidentId",
                table: "SecurityIncidentActions",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidentMembers_IncidentId_UserId",
                table: "SecurityIncidentMembers",
                columns: new[] { "IncidentId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidentMembers_UserId",
                table: "SecurityIncidentMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidents_Reference",
                table: "SecurityIncidents",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidents_ReporterUserId",
                table: "SecurityIncidents",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidents_Status",
                table: "SecurityIncidents",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecurityAffectedItems");

            migrationBuilder.DropTable(
                name: "SecurityIncidentAccesses");

            migrationBuilder.DropTable(
                name: "SecurityIncidentActions");

            migrationBuilder.DropTable(
                name: "SecurityIncidentMembers");

            migrationBuilder.DropTable(
                name: "SecurityIncidents");
        }
    }
}
