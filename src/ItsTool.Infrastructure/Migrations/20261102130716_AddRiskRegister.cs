using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ItsTool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Risks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Threat = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Vulnerability = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExistingControls = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ImpactConfidentiality = table.Column<int>(type: "int", nullable: false),
                    ImpactIntegrity = table.Column<int>(type: "int", nullable: false),
                    ImpactAvailability = table.Column<int>(type: "int", nullable: false),
                    Likelihood = table.Column<int>(type: "int", nullable: false),
                    Treatment = table.Column<int>(type: "int", nullable: false),
                    RecommendedControls = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AnnexAControls = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResidualConfidentiality = table.Column<int>(type: "int", nullable: true),
                    ResidualIntegrity = table.Column<int>(type: "int", nullable: true),
                    ResidualAvailability = table.Column<int>(type: "int", nullable: true),
                    ResidualLikelihood = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    LastReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    DueReminderSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OverdueReminderSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_Risks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiskId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RiskHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskHistory_Risks_RiskId",
                        column: x => x.RiskId,
                        principalTable: "Risks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiskHistory_RiskId",
                table: "RiskHistory",
                column: "RiskId");

            migrationBuilder.CreateIndex(
                name: "IX_Risks_Number",
                table: "Risks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Risks_OwnerUserId",
                table: "Risks",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiskHistory");

            migrationBuilder.DropTable(
                name: "Risks");
        }
    }
}
