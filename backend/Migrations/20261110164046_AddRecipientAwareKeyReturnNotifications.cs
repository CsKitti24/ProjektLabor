using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipientAwareKeyReturnNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ertesitesek_Foglalasok_FoglalasId",
                table: "Ertesitesek");

            migrationBuilder.AlterColumn<int>(
                name: "FoglalasId",
                table: "Ertesitesek",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "CimzettFelhasznaloId",
                table: "Ertesitesek",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Hatarido",
                table: "Ertesitesek",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KulcsmozgasId",
                table: "Ertesitesek",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Olvasva",
                table: "Ertesitesek",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Ertesitesek_CimzettFelhasznaloId",
                table: "Ertesitesek",
                column: "CimzettFelhasznaloId");

            migrationBuilder.CreateIndex(
                name: "IX_Ertesitesek_KulcsmozgasId",
                table: "Ertesitesek",
                column: "KulcsmozgasId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ertesitesek_Felhasznalok_CimzettFelhasznaloId",
                table: "Ertesitesek",
                column: "CimzettFelhasznaloId",
                principalTable: "Felhasznalok",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ertesitesek_Foglalasok_FoglalasId",
                table: "Ertesitesek",
                column: "FoglalasId",
                principalTable: "Foglalasok",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Ertesitesek_Kulcsmozgasok_KulcsmozgasId",
                table: "Ertesitesek",
                column: "KulcsmozgasId",
                principalTable: "Kulcsmozgasok",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ertesitesek_Felhasznalok_CimzettFelhasznaloId",
                table: "Ertesitesek");

            migrationBuilder.DropForeignKey(
                name: "FK_Ertesitesek_Foglalasok_FoglalasId",
                table: "Ertesitesek");

            migrationBuilder.DropForeignKey(
                name: "FK_Ertesitesek_Kulcsmozgasok_KulcsmozgasId",
                table: "Ertesitesek");

            migrationBuilder.DropIndex(
                name: "IX_Ertesitesek_CimzettFelhasznaloId",
                table: "Ertesitesek");

            migrationBuilder.DropIndex(
                name: "IX_Ertesitesek_KulcsmozgasId",
                table: "Ertesitesek");

            migrationBuilder.DropColumn(
                name: "CimzettFelhasznaloId",
                table: "Ertesitesek");

            migrationBuilder.DropColumn(
                name: "Hatarido",
                table: "Ertesitesek");

            migrationBuilder.DropColumn(
                name: "KulcsmozgasId",
                table: "Ertesitesek");

            migrationBuilder.DropColumn(
                name: "Olvasva",
                table: "Ertesitesek");

            migrationBuilder.AlterColumn<int>(
                name: "FoglalasId",
                table: "Ertesitesek",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Ertesitesek_Foglalasok_FoglalasId",
                table: "Ertesitesek",
                column: "FoglalasId",
                principalTable: "Foglalasok",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
