using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// - Ertesitesek.Uzenet: szöveges magyarázat (pl. miért mondták le a foglalást) az oktatónak szóló értesítésben.
    /// - KarbantartasiIdosavok.Leiras legfeljebb 500 karakter (a hosszabb régi leírások levágódnak).
    /// </summary>
    public partial class AddNotificationMessageAndMaintenanceLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [KarbantartasiIdosavok] SET [Leiras] = LEFT([Leiras], 500) WHERE LEN([Leiras]) > 500;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Leiras",
                table: "KarbantartasiIdosavok",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Uzenet",
                table: "Ertesitesek",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Uzenet",
                table: "Ertesitesek");

            migrationBuilder.AlterColumn<string>(
                name: "Leiras",
                table: "KarbantartasiIdosavok",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
