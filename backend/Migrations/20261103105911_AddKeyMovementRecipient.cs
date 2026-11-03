using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddKeyMovementRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AtvevoFelhasznaloId",
                table: "Kulcsmozgasok",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE movement
                SET AtvevoFelhasznaloId = booking.FelhasznaloId
                FROM Kulcsmozgasok AS movement
                INNER JOIN Foglalasok AS booking ON booking.Id = movement.FoglalasId
                WHERE movement.AtvevoFelhasznaloId IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Kulcsmozgasok_AtvevoFelhasznaloId",
                table: "Kulcsmozgasok",
                column: "AtvevoFelhasznaloId");

            migrationBuilder.AddForeignKey(
                name: "FK_Kulcsmozgasok_Felhasznalok_AtvevoFelhasznaloId",
                table: "Kulcsmozgasok",
                column: "AtvevoFelhasznaloId",
                principalTable: "Felhasznalok",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Kulcsmozgasok_Felhasznalok_AtvevoFelhasznaloId",
                table: "Kulcsmozgasok");

            migrationBuilder.DropIndex(
                name: "IX_Kulcsmozgasok_AtvevoFelhasznaloId",
                table: "Kulcsmozgasok");

            migrationBuilder.DropColumn(
                name: "AtvevoFelhasznaloId",
                table: "Kulcsmozgasok");
        }
    }
}
