using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ItsTool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultTypeId",
                table: "Categories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceId",
                table: "Categories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Family = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OwnerGroupId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_ItServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItServices_Groups_OwnerGroupId",
                        column: x => x.OwnerGroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_DefaultTypeId",
                table: "Categories",
                column: "DefaultTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ServiceId",
                table: "Categories",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ItServices_OwnerGroupId",
                table: "ItServices",
                column: "OwnerGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_ItServices_ServiceId",
                table: "Categories",
                column: "ServiceId",
                principalTable: "ItServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_TicketTypes_DefaultTypeId",
                table: "Categories",
                column: "DefaultTypeId",
                principalTable: "TicketTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_ItServices_ServiceId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Categories_TicketTypes_DefaultTypeId",
                table: "Categories");

            migrationBuilder.DropTable(
                name: "ItServices");

            migrationBuilder.DropIndex(
                name: "IX_Categories_DefaultTypeId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_ServiceId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DefaultTypeId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "ServiceId",
                table: "Categories");
        }
    }
}
