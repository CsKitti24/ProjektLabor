using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// A mintaadat két oktatója eddig ugyanazt az 1234 PIN-t kapta. Ez a migráció eltérő PIN-t ad nekik:
    /// oktato → 4729, radli.richard → 8153. Csak akkor módosít, ha a PIN még a mintaadat eredeti
    /// 1234-es értéke, így a felhasználó által időközben beállított PIN megmarad.
    /// </summary>
    public partial class GiveSampleInstructorsDistinctPins : Migration
    {
        private const string Pin1234Hash = "$2a$12$FTjur0SKE1RucNDyrEX6luGNQPRv9DxCW6k1WFl9NUQyZ6yytCEQW";
        private const string Pin4729Hash = "$2a$12$CnYpB5nkG6stzTpW3QKlXuqX53wLiu/XpKleZJioLVfdHAQTzyciG";
        private const string Pin8153Hash = "$2a$12$rfAHed7hGZ5257WCetxtHupv11ER2tzxqSXFMoZv3zVDUpVoUxAg6";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE [Felhasznalok]
                SET [PinHash] = CASE [Username] WHEN N'oktato' THEN N'{Pin4729Hash}' ELSE N'{Pin8153Hash}' END,
                    [PinHibasProbalkozasok] = 0,
                    [PinZarolvaEddig] = NULL,
                    [UpdatedAt] = SYSUTCDATETIME()
                WHERE [Username] IN (N'oktato', N'radli.richard') AND [PinHash] = N'{Pin1234Hash}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE [Felhasznalok]
                SET [PinHash] = N'{Pin1234Hash}', [UpdatedAt] = SYSUTCDATETIME()
                WHERE ([Username] = N'oktato' AND [PinHash] = N'{Pin4729Hash}')
                   OR ([Username] = N'radli.richard' AND [PinHash] = N'{Pin8153Hash}');
                """);
        }
    }
}
