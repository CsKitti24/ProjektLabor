using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// Közös kiinduló adatok minden fejlesztői adatbázisba (a demófiókok helyett):
    /// - szerepkörönként egy felhasználó: admin, oktato, portas, igazgato
    ///   (jelszó: &lt;Szerepkör&gt;jelszo1!, pl. Adminjelszo1!);
    /// - radli.richard (Rádli Richárd) oktató (jelszó: RadliRichard1!), akinek 34 olyan kulcskiadása van, amelyet
    ///   a napi zárásig nem adott vissza (34 × 3 = 102 büntetőpont, a pontszám 100-nál megáll);
    ///   ebből az utolsó két kulcs jelenleg is nála van.
    /// - a korábbi demo-* fiókok inaktiválása (nem törlés, hogy az előzményeik megmaradjanak).
    /// A jelszavak BCrypt-hashként (work factor 12) kerülnek be. Az oktatók kulcsátvételi PIN-je 1234,
    /// ha még nem volt beállítva.
    /// </summary>
    public partial class SeedRoleUsersAndReliabilitySample : Migration
    {
        private const string SampleEmailDomain = "@projektlabor.local";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @pin1234 nvarchar(256) = N'$2a$12$FTjur0SKE1RucNDyrEX6luGNQPRv9DxCW6k1WFl9NUQyZ6yytCEQW';

                DECLARE @seedUsers TABLE (
                    Username nvarchar(50), Nev nvarchar(100), Role nvarchar(max), JelszoHash nvarchar(256), Pin bit);
                INSERT INTO @seedUsers VALUES
                    (N'admin', N'Adminisztrátor', N'Adminisztrator', N'$2a$12$23q0uRMoKUDlL4/dQS/adu.n.7obQn/GEpOL8A8g55Kk9hVjTf.Va', 0),
                    (N'oktato', N'Oktató', N'Oktato', N'$2a$12$ysGT0/JdNYd5OITODFM39O99A1U26i39gAYc78xkY.4qA3Z9dg.YW', 1),
                    (N'portas', N'Portás', N'Portas', N'$2a$12$01raKmkR/7zM.ir.imSfJeqb9mFAI1eQiL0h69VranJfdzLrYQhza', 0),
                    (N'igazgato', N'Üzemeltetési igazgató', N'UzemeltetesiIgazgato', N'$2a$12$.g.1NVsA0WE6.pHDjL1vzOdTKgUG0LftPmNsWlCDUKk0J3kYpq2Ye', 0),
                    (N'radli.richard', N'Rádli Richárd', N'Oktato', N'$2a$12$/.w79qsFMyAn6Z0iizLEXOZIprTqrBi/DPJQz/9AyQdzsTA9cTs/2', 1);

                -- Ha a felhasználónév már létezik, a jelszót, a szerepkört és az aktív állapotot igazítjuk.
                UPDATE target
                SET [JelszoHash] = source.[JelszoHash],
                    [Role] = source.[Role],
                    [Aktiv] = 1,
                    [PinHash] = CASE WHEN source.[Pin] = 1 AND target.[PinHash] IS NULL THEN @pin1234 ELSE target.[PinHash] END,
                    [UpdatedAt] = @now
                FROM [Felhasznalok] AS target
                INNER JOIN @seedUsers AS source ON source.[Username] = target.[Username];

                INSERT INTO [Felhasznalok]
                    ([Username], [Email], [JelszoHash], [PinHash], [Nev], [Role], [Aktiv], [PinHibasProbalkozasok], [CreatedAt], [UpdatedAt])
                SELECT source.[Username],
                       source.[Username] + N'{{SampleEmailDomain}}',
                       source.[JelszoHash],
                       CASE WHEN source.[Pin] = 1 THEN @pin1234 END,
                       source.[Nev],
                       source.[Role],
                       1,
                       0,
                       @now,
                       @now
                FROM @seedUsers AS source
                WHERE NOT EXISTS (SELECT 1 FROM [Felhasznalok] AS existing WHERE existing.[Username] = source.[Username]);

                -- A korábbi demófiókok inaktiválása.
                UPDATE [Felhasznalok]
                SET [Aktiv] = 0, [UpdatedAt] = @now
                WHERE [Username] IN (N'demo-oktato', N'demo-portas', N'demo-admin', N'demo-igazgato');

                -- Rádli Richárd kulcskiadásai két mintaterem kulcsával.
                DECLARE @radliId int = (SELECT [Id] FROM [Felhasznalok] WHERE [Username] = N'radli.richard');
                DECLARE @portasId int = (SELECT [Id] FROM [Felhasznalok] WHERE [Username] = N'portas');
                DECLARE @tz nvarchar(64) = N'Central European Standard Time';
                DECLARE @todayLocal date = CAST(CONVERT(datetime2, @now AT TIME ZONE 'UTC' AT TIME ZONE @tz) AS date);
                DECLARE @startYear int = CASE WHEN MONTH(@now) >= 9 THEN YEAR(@now) ELSE YEAR(@now) - 1 END;
                DECLARE @tanev nvarchar(20) = CONCAT(@startYear, N'/', @startYear + 1);

                INSERT INTO [Termek] ([Nev], [Kapacitas], [Leiras], [Felszereltseg], [Aktivalva])
                VALUES (N'B-101', 30, N'Mintaterem', N'Projektor', 1);
                DECLARE @room1 int = CAST(SCOPE_IDENTITY() AS int);
                INSERT INTO [Termek] ([Nev], [Kapacitas], [Leiras], [Felszereltseg], [Aktivalva])
                VALUES (N'B-102', 24, N'Mintaterem', N'Számítógépek', 1);
                DECLARE @room2 int = CAST(SCOPE_IDENTITY() AS int);

                INSERT INTO [Kulcsok] ([Szam], [Tipus], [Aktivalva], [TeremId]) VALUES (N'MINTA-B101', N'Osztalykulcs', 1, @room1);
                DECLARE @key1 int = CAST(SCOPE_IDENTITY() AS int);
                INSERT INTO [Kulcsok] ([Szam], [Tipus], [Aktivalva], [TeremId]) VALUES (N'MINTA-B102', N'Osztalykulcs', 1, @room2);
                DECLARE @key2 int = CAST(SCOPE_IDENTITY() AS int);

                -- 34 nap, naponta egy kiadás felváltva a két kulccsal, 08:30-kor (helyi idő).
                -- A kulcs a 22:00-s napi zárásig nem került vissza (+3 pont), csak másnap reggel;
                -- a két utolsó kiadás kulcsa máig nincs visszaadva.
                DECLARE @issueCount int = 34;
                DECLARE @i int = 1;
                WHILE @i <= @issueCount
                BEGIN
                    DECLARE @keyId int = CASE WHEN @i % 2 = 1 THEN @key1 ELSE @key2 END;
                    DECLARE @roomId int = CASE WHEN @i % 2 = 1 THEN @room1 ELSE @room2 END;
                    DECLARE @dayLocal datetime2 = CAST(DATEADD(day, @i - @issueCount - 1, @todayLocal) AS datetime2);
                    DECLARE @issuedAt datetime2 = CONVERT(datetime2, DATEADD(minute, 510, @dayLocal) AT TIME ZONE @tz AT TIME ZONE 'UTC');
                    DECLARE @deadline datetime2 = CONVERT(datetime2, DATEADD(hour, 22, @dayLocal) AT TIME ZONE @tz AT TIME ZONE 'UTC');

                    INSERT INTO [Kulcsmozgasok]
                        ([KulcsId], [FoglalasId], [TeremId], [AtvevoFelhasznaloId], [MozgasTipus], [Idopont], [OperátorId], [Megjegyzes], [TeremAllapot], [CreatedAt])
                    VALUES (@keyId, NULL, @roomId, @radliId, N'Kiadás', @issuedAt, @portasId, N'Mintaadat', N'Rendben', @issuedAt);
                    DECLARE @movementId int = CAST(SCOPE_IDENTITY() AS int);

                    INSERT INTO [MegbizhatosagiEsemenyek]
                        ([FelhasznaloId], [KulcsId], [FoglalasId], [KiadasiKulcsmozgasId], [EsemenyTipus], [Indoklas],
                         [EredetiHatarido], [Pont], [Automatikus], [EsemenyIdopontja], [Tanev], [CreatedAt])
                    VALUES (@radliId, @keyId, NULL, @movementId, N'LeNemAdott', N'A kulcs a napi zárási időpontig nem került vissza.',
                            @deadline, 3, 1, @deadline, @tanev, @deadline);

                    IF @i <= @issueCount - 2
                    BEGIN
                        DECLARE @returnedAt datetime2 = CONVERT(datetime2, DATEADD(minute, 1905, @dayLocal) AT TIME ZONE @tz AT TIME ZONE 'UTC');
                        INSERT INTO [Kulcsmozgasok]
                            ([KulcsId], [FoglalasId], [TeremId], [AtvevoFelhasznaloId], [MozgasTipus], [Idopont], [OperátorId], [Megjegyzes], [TeremAllapot], [CreatedAt])
                        VALUES (@keyId, NULL, @roomId, @radliId, N'Visszavétel', @returnedAt, @portasId, N'Késve visszahozva', N'Rendben', @returnedAt);
                    END

                    SET @i += 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DECLARE @sampleKeys TABLE (Id int);
                INSERT INTO @sampleKeys SELECT [Id] FROM [Kulcsok] WHERE [Szam] IN (N'MINTA-B101', N'MINTA-B102');

                DELETE FROM [MegbizhatosagiEsemenyek] WHERE [KulcsId] IN (SELECT [Id] FROM @sampleKeys);
                DELETE FROM [Ertesitesek] WHERE [KulcsmozgasId] IN
                    (SELECT [Id] FROM [Kulcsmozgasok] WHERE [KulcsId] IN (SELECT [Id] FROM @sampleKeys));
                DELETE FROM [Kulcsmozgasok] WHERE [KulcsId] IN (SELECT [Id] FROM @sampleKeys);
                DELETE FROM [Kulcsok] WHERE [Id] IN (SELECT [Id] FROM @sampleKeys);
                DELETE FROM [Termek] WHERE [Nev] IN (N'B-101', N'B-102') AND [Leiras] = N'Mintaterem'
                    AND NOT EXISTS (SELECT 1 FROM [Kulcsok] WHERE [Kulcsok].[TeremId] = [Termek].[Id]);

                -- Az e migráció által létrehozott (mintaadat e-mail-című) fiókokat inaktiváljuk; törölni nem
                -- lehet őket, mert a csak bővíthető audit napló (pl. bejelentkezések) hivatkozhat rájuk.
                UPDATE [Felhasznalok] SET [Aktiv] = 0
                WHERE [Email] IN (N'admin{SampleEmailDomain}', N'oktato{SampleEmailDomain}', N'portas{SampleEmailDomain}',
                                  N'igazgato{SampleEmailDomain}', N'radli.richard{SampleEmailDomain}');

                UPDATE [Felhasznalok] SET [Aktiv] = 1
                WHERE [Username] IN (N'demo-oktato', N'demo-portas', N'demo-admin', N'demo-igazgato');
                """);
        }
    }
}
