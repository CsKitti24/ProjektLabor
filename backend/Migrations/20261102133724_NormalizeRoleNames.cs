using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeRoleNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [Felhasznalok]
                SET [Role] = CASE [Role]
                    WHEN N'Oktató / Igénylő' THEN N'Oktato'
                    WHEN N'Portás / Operátor' THEN N'Portas'
                    WHEN N'Adminisztrátor' THEN N'Adminisztrator'
                    WHEN N'Üzemeltetési Igazgató' THEN N'UzemeltetesiIgazgato'
                END
                WHERE [Role] IN (
                    N'Oktató / Igénylő',
                    N'Portás / Operátor',
                    N'Adminisztrátor',
                    N'Üzemeltetési Igazgató');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [Felhasznalok]
                SET [Role] = CASE [Role]
                    WHEN N'Oktato' THEN N'Oktató / Igénylő'
                    WHEN N'Portas' THEN N'Portás / Operátor'
                    WHEN N'Adminisztrator' THEN N'Adminisztrátor'
                    WHEN N'UzemeltetesiIgazgato' THEN N'Üzemeltetési Igazgató'
                END
                WHERE [Role] IN (
                    N'Oktato',
                    N'Portas',
                    N'Adminisztrator',
                    N'UzemeltetesiIgazgato');
                """);
        }
    }
}
