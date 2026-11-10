using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class BackfillMasterKeyRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO [KulcsTermek] ([KulcsId], [TeremId])
                SELECT [Kulcsok].[Id], [Kulcsok].[TeremId]
                FROM [Kulcsok]
                WHERE [Kulcsok].[Tipus] = N'Mesterkulcs'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [KulcsTermek]
                      WHERE [KulcsTermek].[KulcsId] = [Kulcsok].[Id]
                        AND [KulcsTermek].[TeremId] = [Kulcsok].[TeremId]
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Existing and newly assigned room rights must not be deleted on rollback.
        }
    }
}
