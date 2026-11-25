using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingStatusIndexAndBookingSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FelfuggesztesOka",
                table: "Felhasznalok",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FoglalasFelfuggesztveEddig",
                table: "Felhasznalok",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Foglalasok_Status_StartTime",
                table: "Foglalasok",
                columns: new[] { "Status", "StartTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Foglalasok_Status_StartTime",
                table: "Foglalasok");

            migrationBuilder.DropColumn(
                name: "FelfuggesztesOka",
                table: "Felhasznalok");

            migrationBuilder.DropColumn(
                name: "FoglalasFelfuggesztveEddig",
                table: "Felhasznalok");
        }
    }
}
