using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Services;

/// <summary>
/// Rendszerbeli értesítés az oktatónak, ha a foglalását más (adminisztrátor, karbantartás, terem
/// deaktiválása) mondta le vagy módosította. A saját műveleteiről az oktató nem kap értesítést.
/// </summary>
public static class BookingNotifications
{
    public const string CancelledType = "BookingCancelled";
    public const string ChangedType = "BookingChanged";
    public const int MaxMessageLength = 500;

    public static void AddCancelled(AppDbContext db, Foglalas booking, string reason, DateTime nowUtc) =>
        Add(db, booking, CancelledType, reason, nowUtc);

    public static void AddChanged(AppDbContext db, Foglalas booking, string description, DateTime nowUtc) =>
        Add(db, booking, ChangedType, description, nowUtc);

    private static void Add(AppDbContext db, Foglalas booking, string type, string message, DateTime nowUtc)
    {
        db.Ertesitesek.Add(new Ertesites
        {
            Foglalas = booking,
            FoglalasId = booking.Id == 0 ? null : booking.Id,
            CimzettFelhasznaloId = booking.FelhasznaloId,
            ErtesitesTipus = type,
            IdoPont = nowUtc,
            Allapot = "Kesz",
            Uzenet = message.Length > MaxMessageLength ? message[..MaxMessageLength] : message,
            CreatedAt = nowUtc
        });
    }
}
