using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterKeyAccessUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MesterkulcsJogosultsagok_KulcsId",
                table: "MesterkulcsJogosultsagok");

            migrationBuilder.AlterColumn<string>(
                name: "Leiras",
                table: "Termek",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Megjegyzes",
                table: "Kulcsmozgasok",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_MesterkulcsJogosultsagok_KulcsId_FelhasznaloId",
                table: "MesterkulcsJogosultsagok",
                columns: new[] { "KulcsId", "FelhasznaloId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MesterkulcsJogosultsagok_KulcsId_FelhasznaloId",
                table: "MesterkulcsJogosultsagok");

            migrationBuilder.AlterColumn<string>(
                name: "Leiras",
                table: "Termek",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Megjegyzes",
                table: "Kulcsmozgasok",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MesterkulcsJogosultsagok_KulcsId",
                table: "MesterkulcsJogosultsagok",
                column: "KulcsId");
        }
    }
}
