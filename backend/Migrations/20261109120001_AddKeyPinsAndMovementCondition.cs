using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddKeyPinsAndMovementCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SerulesLeiras",
                table: "Kulcsmozgasok",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeremAllapot",
                table: "Kulcsmozgasok",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinHash",
                table: "Felhasznalok",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SerulesLeiras",
                table: "Kulcsmozgasok");

            migrationBuilder.DropColumn(
                name: "TeremAllapot",
                table: "Kulcsmozgasok");

            migrationBuilder.DropColumn(
                name: "PinHash",
                table: "Felhasznalok");
        }
    }
}
