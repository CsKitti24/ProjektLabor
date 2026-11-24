using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// - JelszoValtozas: a jelszócsere előtt kiadott bejelentkezési tokenek érvénytelenítéséhez.
    /// - Egyedi teremnév: a meglévő azonos nevű termek közül a nem minta-, legkorábbi terem megtartja a nevét,
    ///   a többi neve mögé az azonosítója kerül (pl. "B-101 (#57)").
    /// - Indexek a kulcs legutolsó mozgásához, a terem ütköző foglalásaihoz és az auditnapló időrendjéhez;
    ///   az állapot/szerepkör/típus oszlopok nvarchar(max) helyett nvarchar(50).
    /// - Az auditnapló magyar szöveges művelettípusai egységes, angol kódokra cserélődnek.
    /// </summary>
    public partial class HardenSchemaAndAuditNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT [Id], [Nev],
                           ROW_NUMBER() OVER (
                               PARTITION BY [Nev]
                               ORDER BY CASE WHEN [Leiras] = N'Mintaterem' THEN 1 ELSE 0 END, [Id]) AS [Sorszam]
                    FROM [Termek])
                UPDATE ranked
                SET [Nev] = CONCAT(LEFT([Nev], 85), N' (#', [Id], N')')
                WHERE [Sorszam] > 1;
                """);

            migrationBuilder.Sql("""
                UPDATE [AuditLogok] SET [MuveletTipus] = CASE [MuveletTipus]
                    WHEN N'Rendszerparaméter módosítása' THEN N'SYSTEM_PARAMETER_UPDATE'
                    WHEN N'Pontkorrekció' THEN N'RELIABILITY_CORRECTION'
                    WHEN N'Automatizálási adatprobléma' THEN N'AUTOMATION_DATA_ISSUE'
                    WHEN N'Automatikus büntetőpont' THEN N'AUTOMATIC_PENALTY'
                    ELSE [MuveletTipus] END
                WHERE [MuveletTipus] IN (N'Rendszerparaméter módosítása', N'Pontkorrekció',
                                         N'Automatizálási adatprobléma', N'Automatikus büntetőpont');
                """);

            migrationBuilder.DropIndex(
                name: "IX_Kulcsmozgasok_KulcsId",
                table: "Kulcsmozgasok");

            migrationBuilder.DropIndex(
                name: "IX_Foglalasok_TeremId",
                table: "Foglalasok");

            migrationBuilder.AlterColumn<string>(
                name: "MozgasTipus",
                table: "Kulcsmozgasok",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Statusz",
                table: "Hibajegyek",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Foglalasok",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Felhasznalok",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "JelszoValtozas",
                table: "Felhasznalok",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErtesitesTipus",
                table: "Ertesitesek",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Allapot",
                table: "Ertesitesek",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Termek_Nev",
                table: "Termek",
                column: "Nev",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kulcsmozgasok_KulcsId_Idopont_Id",
                table: "Kulcsmozgasok",
                columns: new[] { "KulcsId", "Idopont", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Foglalasok_TeremId_StartTime_EndTime",
                table: "Foglalasok",
                columns: new[] { "TeremId", "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogok_LetrehozasIdeje",
                table: "AuditLogok",
                column: "LetrehozasIdeje");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [AuditLogok] SET [MuveletTipus] = CASE [MuveletTipus]
                    WHEN N'SYSTEM_PARAMETER_UPDATE' THEN N'Rendszerparaméter módosítása'
                    WHEN N'RELIABILITY_CORRECTION' THEN N'Pontkorrekció'
                    WHEN N'AUTOMATION_DATA_ISSUE' THEN N'Automatizálási adatprobléma'
                    WHEN N'AUTOMATIC_PENALTY' THEN N'Automatikus büntetőpont'
                    ELSE [MuveletTipus] END
                WHERE [MuveletTipus] IN (N'SYSTEM_PARAMETER_UPDATE', N'RELIABILITY_CORRECTION',
                                         N'AUTOMATION_DATA_ISSUE', N'AUTOMATIC_PENALTY');
                """);

            migrationBuilder.DropIndex(
                name: "IX_Termek_Nev",
                table: "Termek");

            migrationBuilder.DropIndex(
                name: "IX_Kulcsmozgasok_KulcsId_Idopont_Id",
                table: "Kulcsmozgasok");

            migrationBuilder.DropIndex(
                name: "IX_Foglalasok_TeremId_StartTime_EndTime",
                table: "Foglalasok");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogok_LetrehozasIdeje",
                table: "AuditLogok");

            migrationBuilder.DropColumn(
                name: "JelszoValtozas",
                table: "Felhasznalok");

            migrationBuilder.AlterColumn<string>(
                name: "MozgasTipus",
                table: "Kulcsmozgasok",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Statusz",
                table: "Hibajegyek",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Foglalasok",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Felhasznalok",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "ErtesitesTipus",
                table: "Ertesitesek",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Allapot",
                table: "Ertesitesek",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "IX_Kulcsmozgasok_KulcsId",
                table: "Kulcsmozgasok",
                column: "KulcsId");

            migrationBuilder.CreateIndex(
                name: "IX_Foglalasok_TeremId",
                table: "Foglalasok",
                column: "TeremId");
        }
    }
}
