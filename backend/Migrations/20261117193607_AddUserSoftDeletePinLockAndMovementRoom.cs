using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSoftDeletePinLockAndMovementRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeremId",
                table: "Kulcsmozgasok",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Aktiv",
                table: "Felhasznalok",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PinHibasProbalkozasok",
                table: "Felhasznalok",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinZarolvaEddig",
                table: "Felhasznalok",
                type: "datetime2",
                nullable: true);

            // Meglévő mozgások terme: a foglalás terme, ennek hiányában osztálykulcsnál a kulcs saját terme.
            // Foglalás nélkül kiadott mesterkulcsnál a tényleges terem korábban nem volt tárolva, ezért
            // ott NULL marad (ismeretlen); visszavételkor a portás választja ki a termet.
            migrationBuilder.Sql("""
                UPDATE movement
                SET [TeremId] = COALESCE(
                    booking.[TeremId],
                    CASE WHEN keyItem.[Tipus] <> N'Mesterkulcs' THEN keyItem.[TeremId] END)
                FROM [Kulcsmozgasok] AS movement
                INNER JOIN [Kulcsok] AS keyItem ON keyItem.[Id] = movement.[KulcsId]
                LEFT JOIN [Foglalasok] AS booking ON booking.[Id] = movement.[FoglalasId]
                WHERE movement.[TeremId] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeremId",
                table: "Kulcsmozgasok");

            migrationBuilder.DropColumn(
                name: "Aktiv",
                table: "Felhasznalok");

            migrationBuilder.DropColumn(
                name: "PinHibasProbalkozasok",
                table: "Felhasznalok");

            migrationBuilder.DropColumn(
                name: "PinZarolvaEddig",
                table: "Felhasznalok");
        }
    }
}
