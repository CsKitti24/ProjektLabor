using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// A mintaoktató (radli.richard) kulcskiadásai eddig foglalás nélkül szerepeltek, ezért a saját
    /// foglalásai között nem látszottak. Ez a migráció minden ilyen kiadáshoz létrehoz egy lezárt
    /// (Kesz) foglalást ugyanarra a napra és teremre (08:45–10:15, helyi idő), és ehhez köti a kiadást,
    /// a hozzá tartozó visszavételt és a büntetőpontot is.
    /// </summary>
    public partial class LinkSampleKeyIssuesToBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @radliId int = (SELECT [Id] FROM [Felhasznalok] WHERE [Username] = N'radli.richard');
                DECLARE @tz nvarchar(64) = N'Central European Standard Time';
                DECLARE @movementId int, @keyId int, @roomId int, @issuedAt datetime2;

                DECLARE issue_cursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT movement.[Id], movement.[KulcsId], movement.[TeremId], movement.[Idopont]
                    FROM [Kulcsmozgasok] AS movement
                    INNER JOIN [Kulcsok] AS keyItem ON keyItem.[Id] = movement.[KulcsId]
                    WHERE keyItem.[Szam] IN (N'MINTA-B101', N'MINTA-B102')
                      AND movement.[AtvevoFelhasznaloId] = @radliId
                      AND movement.[MozgasTipus] = N'Kiadás'
                      AND movement.[FoglalasId] IS NULL
                    ORDER BY movement.[Idopont], movement.[Id];

                OPEN issue_cursor;
                FETCH NEXT FROM issue_cursor INTO @movementId, @keyId, @roomId, @issuedAt;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    DECLARE @dayLocal datetime2 = CAST(CAST(CONVERT(datetime2, @issuedAt AT TIME ZONE 'UTC' AT TIME ZONE @tz) AS date) AS datetime2);
                    DECLARE @start datetime2 = CONVERT(datetime2, DATEADD(minute, 525, @dayLocal) AT TIME ZONE @tz AT TIME ZONE 'UTC');
                    DECLARE @end datetime2 = CONVERT(datetime2, DATEADD(minute, 615, @dayLocal) AT TIME ZONE @tz AT TIME ZONE 'UTC');

                    INSERT INTO [Foglalasok] ([FelhasznaloId], [TeremId], [StartTime], [EndTime], [Status], [CreatedAt], [UpdatedAt])
                    VALUES (@radliId, @roomId, @start, @end, N'Kesz', DATEADD(day, -1, @start), @end);
                    DECLARE @bookingId int = CAST(SCOPE_IDENTITY() AS int);

                    UPDATE [Kulcsmozgasok] SET [FoglalasId] = @bookingId WHERE [Id] = @movementId;
                    UPDATE [Kulcsmozgasok] SET [FoglalasId] = @bookingId
                    WHERE [Id] = (
                        SELECT TOP 1 [Id] FROM [Kulcsmozgasok]
                        WHERE [KulcsId] = @keyId AND [MozgasTipus] = N'Visszavétel' AND [Idopont] > @issuedAt
                        ORDER BY [Idopont], [Id]);
                    UPDATE [MegbizhatosagiEsemenyek] SET [FoglalasId] = @bookingId WHERE [KiadasiKulcsmozgasId] = @movementId;

                    FETCH NEXT FROM issue_cursor INTO @movementId, @keyId, @roomId, @issuedAt;
                END
                CLOSE issue_cursor;
                DEALLOCATE issue_cursor;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @radliId int = (SELECT [Id] FROM [Felhasznalok] WHERE [Username] = N'radli.richard');
                DECLARE @sampleBookings TABLE (Id int);
                INSERT INTO @sampleBookings
                SELECT DISTINCT movement.[FoglalasId]
                FROM [Kulcsmozgasok] AS movement
                INNER JOIN [Kulcsok] AS keyItem ON keyItem.[Id] = movement.[KulcsId]
                INNER JOIN [Foglalasok] AS booking ON booking.[Id] = movement.[FoglalasId]
                WHERE keyItem.[Szam] IN (N'MINTA-B101', N'MINTA-B102')
                  AND booking.[FelhasznaloId] = @radliId;

                UPDATE [MegbizhatosagiEsemenyek] SET [FoglalasId] = NULL WHERE [FoglalasId] IN (SELECT [Id] FROM @sampleBookings);
                UPDATE [Kulcsmozgasok] SET [FoglalasId] = NULL WHERE [FoglalasId] IN (SELECT [Id] FROM @sampleBookings);
                DELETE FROM [Foglalasok] WHERE [Id] IN (SELECT [Id] FROM @sampleBookings);
                """);
        }
    }
}
