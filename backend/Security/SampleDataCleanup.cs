using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;

namespace ProjektLabor.Security;

/// <summary>
/// A SeedRoleUsersAndReliabilitySample migráció minden adatbázisba betesz két mintatermet (B-101, B-102),
/// két mintakulcsot (MINTA-B101, MINTA-B102), 34 kitalált kulcskiadást, a hozzájuk tartozó lezárt
/// foglalásokat és büntetőpontokat. Éles üzemben (SampleAccounts:Enabled != true) ezek hamis adatot
/// jelentenének (két örökké „kiadott” kulcs, torz riportok, esti értesítések), ezért induláskor töröljük őket —
/// de csak akkor, ha a mintakulcsokat azóta senki nem használta valódi kiadásra.
/// </summary>
public static class SampleDataCleanup
{
    public static readonly string[] SampleKeyNumbers = ["MINTA-B101", "MINTA-B102"];
    public const string SampleRoomDescription = "Mintaterem";

    // A mintaadat kulcsmozgásainak megjegyzései: ezekről ismerjük fel, hogy a mozgás nem valódi.
    private static readonly string[] SampleMovementNotes = ["Mintaadat", "Késve visszahozva"];

    public static async Task RemoveUntouchedSampleDataAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var keyIds = await db.Kulcsok
            .Where(key => SampleKeyNumbers.Contains(key.Szam))
            .Select(key => key.Id)
            .ToListAsync(cancellationToken);
        if (keyIds.Count == 0)
        {
            return;
        }

        var movements = await db.Kulcsmozgasok
            .Where(movement => keyIds.Contains(movement.KulcsId))
            .ToListAsync(cancellationToken);
        var hasRealUsage =
            movements.Any(movement => movement.Megjegyzes is null || !SampleMovementNotes.Contains(movement.Megjegyzes)) ||
            await db.MesterkulcsJogosultsagok.AnyAsync(access => keyIds.Contains(access.KulcsId), cancellationToken);
        if (hasRealUsage)
        {
            logger.LogWarning(
                "The sample keys {Keys} have real key movements or permissions, so the sample data was kept. Remove it manually if it is not needed.",
                string.Join(", ", SampleKeyNumbers));
            return;
        }

        var movementIds = movements.Select(movement => movement.Id).ToList();
        var bookingIds = movements
            .Where(movement => movement.FoglalasId.HasValue)
            .Select(movement => movement.FoglalasId!.Value)
            .Distinct()
            .ToList();
        var roomIds = await db.Kulcsok
            .Where(key => keyIds.Contains(key.Id))
            .Select(key => key.TeremId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                ? null
                : await db.Database.BeginTransactionAsync(cancellationToken);

            db.Ertesitesek.RemoveRange(await db.Ertesitesek
                .Where(notification =>
                    (notification.KulcsmozgasId.HasValue && movementIds.Contains(notification.KulcsmozgasId.Value)) ||
                    (notification.FoglalasId.HasValue && bookingIds.Contains(notification.FoglalasId.Value)))
                .ToListAsync(cancellationToken));
            db.MegbizhatosagiEsemenyek.RemoveRange(await db.MegbizhatosagiEsemenyek
                .Where(item =>
                    (item.KulcsId.HasValue && keyIds.Contains(item.KulcsId.Value)) ||
                    (item.KiadasiKulcsmozgasId.HasValue && movementIds.Contains(item.KiadasiKulcsmozgasId.Value)))
                .ToListAsync(cancellationToken));
            db.Kulcsmozgasok.RemoveRange(movements);
            await db.SaveChangesAsync(cancellationToken);

            // A mintakiadásokhoz létrehozott, lezárt foglalások (más nem hivatkozik rájuk).
            db.Foglalasok.RemoveRange(await db.Foglalasok
                .Where(booking => bookingIds.Contains(booking.Id) &&
                                  !db.Kulcsmozgasok.Any(movement => movement.FoglalasId == booking.Id) &&
                                  !db.MegbizhatosagiEsemenyek.Any(item => item.FoglalasId == booking.Id) &&
                                  !db.Ertesitesek.Any(notification => notification.FoglalasId == booking.Id))
                .ToListAsync(cancellationToken));
            db.KulcsTermek.RemoveRange(await db.KulcsTermek
                .Where(link => keyIds.Contains(link.KulcsId))
                .ToListAsync(cancellationToken));
            db.Kulcsok.RemoveRange(await db.Kulcsok
                .Where(key => keyIds.Contains(key.Id))
                .ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);

            // A mintatermet csak akkor töröljük, ha semmi más (foglalás, kulcs, karbantartás, hibajegy) nem hivatkozik rá.
            db.Termek.RemoveRange(await db.Termek
                .Where(room => roomIds.Contains(room.Id) &&
                               room.Leiras == SampleRoomDescription &&
                               !db.Kulcsok.Any(key => key.TeremId == room.Id) &&
                               !db.KulcsTermek.Any(link => link.TeremId == room.Id) &&
                               !db.Foglalasok.Any(booking => booking.TeremId == room.Id) &&
                               !db.KarbantartasiIdosavok.Any(window => window.TeremId == room.Id) &&
                               !db.Hibajegyek.Any(ticket => ticket.TeremId == room.Id) &&
                               !db.Kulcsmozgasok.Any(movement => movement.TeremId == room.Id))
                .ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        });

        logger.LogWarning(
            "Removed the sample data of the keys {Keys} ({MovementCount} key movements, {BookingCount} bookings). " +
            "Set SampleAccounts:Enabled=true only for development or demonstrations.",
            string.Join(", ", SampleKeyNumbers),
            movementIds.Count,
            bookingIds.Count);
    }
}
