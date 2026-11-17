using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProjektLabor.Models;

namespace ProjektLabor.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Felhasznalo> Felhasznalok { get; set; }
        public DbSet<Terem> Termek { get; set; }
        public DbSet<Kulcs> Kulcsok { get; set; }
        public DbSet<MesterkulcsJogosultsag> MesterkulcsJogosultsagok { get; set; }
        public DbSet<Foglalas> Foglalasok { get; set; }
        public DbSet<Kulcsmozgas> Kulcsmozgasok { get; set; }
        public DbSet<Ertesites> Ertesitesek { get; set; }
        public DbSet<Hibajegy> Hibajegyek { get; set; }
        public DbSet<KarbantartasiIdosav> KarbantartasiIdosavok { get; set; }
        public DbSet<MegbizhatosagiEsemeny> MegbizhatosagiEsemenyek { get; set; }
        public DbSet<AuditLog> AuditLogok { get; set; }
        public DbSet<Rendszerparameter> Rendszerparameterek { get; set; }
        public DbSet<KulcsTerem> KulcsTermek { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            // Minden időpont UTC-ben tárolódik; olvasáskor UTC kind-ot kap, így a JSON "Z" jelöléssel megy ki.
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // A születési dátum naptári dátum, nem időpont: nem konvertáljuk időzónák között.
            modelBuilder.Entity<Felhasznalo>()
                .Property(u => u.SzuletesiDatum)
                .HasConversion((ValueConverter?)null);
            // Unique constraints for Felhasznalo
            modelBuilder.Entity<Felhasznalo>()
                .HasIndex(u => u.Username).IsUnique();
            modelBuilder.Entity<Felhasznalo>()
                .HasIndex(u => u.Email).IsUnique();

            modelBuilder.Entity<AuditLog>()
                .HasIndex(entry => entry.AutomationKey)
                .IsUnique()
                .HasFilter("[AutomationKey] IS NOT NULL");

            modelBuilder.Entity<Ertesites>()
                .HasIndex(notification => notification.AutomationKey)
                .IsUnique()
                .HasFilter("[AutomationKey] IS NOT NULL");

            modelBuilder.Entity<MegbizhatosagiEsemeny>()
                .HasIndex(item => item.KiadasiKulcsmozgasId)
                .IsUnique()
                .HasFilter("[Automatikus] = 1 AND [KiadasiKulcsmozgasId] IS NOT NULL");

            modelBuilder.Entity<Kulcs>()
                .HasIndex(key => key.Szam).IsUnique();

            modelBuilder.Entity<MesterkulcsJogosultsag>()
                .HasIndex(access => new { access.KulcsId, access.FelhasznaloId })
                .IsUnique();

            // Many-to-many between Kulcs (master keys) and Terem (rooms) for access rights
            modelBuilder.Entity<KulcsTerem>()
                .HasKey(kt => new { kt.KulcsId, kt.TeremId });
            modelBuilder.Entity<KulcsTerem>()
                .HasOne(kt => kt.Kulcs)
                .WithMany(k => k.KulcsTeremKapcsok)
                .HasForeignKey(kt => kt.KulcsId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<KulcsTerem>()
                .HasOne(kt => kt.Terem)
                .WithMany(t => t.KulcsTeremKapcsok)
                .HasForeignKey(kt => kt.TeremId)
                .OnDelete(DeleteBehavior.NoAction);

            // Explicit relationships for models with multiple references or navigation ambiguities
            modelBuilder.Entity<MegbizhatosagiEsemeny>()
                .HasOne(m => m.Felhasznalo)
                .WithMany(u => u.MegbizhatosagiEsemenyek)
                .HasForeignKey(m => m.FelhasznaloId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MegbizhatosagiEsemeny>()
                .HasOne(m => m.LetrehozoFelhasznalo)
                .WithMany()
                .HasForeignKey(m => m.LetrehozoFelhasznaloId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Kulcsmozgas>()
                .HasOne(k => k.Operátor)
                .WithMany(u => u.Kulcsmozgasok)
                .HasForeignKey(k => k.OperátorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Kulcsmozgas>()
                .HasOne(movement => movement.AtvevoFelhasznalo)
                .WithMany()
                .HasForeignKey(movement => movement.AtvevoFelhasznaloId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Ertesites>()
                .HasOne(notification => notification.CimzettFelhasznalo)
                .WithMany()
                .HasForeignKey(notification => notification.CimzettFelhasznaloId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Ertesites>()
                .HasOne(notification => notification.Foglalas)
                .WithMany(booking => booking.Ertesitesek)
                .HasForeignKey(notification => notification.FoglalasId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Ertesites>()
                .HasOne(notification => notification.Kulcsmozgas)
                .WithMany()
                .HasForeignKey(notification => notification.KulcsmozgasId)
                .OnDelete(DeleteBehavior.Restrict);

            // Seed default system parameters
            modelBuilder.Entity<Rendszerparameter>().HasData(
                new Rendszerparameter { Kulcs = "napi_kulcsleadasi_zarasi_idopont", Ertek = "22:00", Leiras = "Napi kulcsleadási zárási időpont", ModositasIdeje = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new Rendszerparameter { Kulcs = "no_show_idokorlat_perc", Ertek = "15", Leiras = "No-show időkorlát percben", ModositasIdeje = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
            );
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ValidateBookingsAsync(CancellationToken.None).GetAwaiter().GetResult();
            ValidateAuditLogAppendOnly();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            await ValidateBookingsAsync(cancellationToken);
            ValidateAuditLogAppendOnly();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ValidateAuditLogAppendOnly()
        {
            if (ChangeTracker.Entries<AuditLog>().Any(entry =>
                    entry.State is Microsoft.EntityFrameworkCore.EntityState.Modified or
                        Microsoft.EntityFrameworkCore.EntityState.Deleted))
            {
                throw new InvalidOperationException("Audit log entries are append-only and cannot be changed or deleted.");
            }
        }

        // Utolsó védvonal az átfedő foglalások ellen. A párhuzamos kérések sorbarendezését
        // a hívó végpontok sp_getapplock + Serializable tranzakcióval végzik.
        private async Task ValidateBookingsAsync(CancellationToken cancellationToken)
        {
            var changedBookings = ChangeTracker.Entries<Foglalas>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified)
                .Select(e => e.Entity)
                .Where(booking => Services.BookingRules.IsActive(booking.Status))
                .ToList();
            foreach (var booking in changedBookings)
            {
                var start = Services.AppTime.ToUtc(booking.StartTime);
                var end = Services.AppTime.ToUtc(booking.EndTime);
                var conflict = await Foglalasok
                    .Where(Services.BookingRules.ActiveBookings)
                    .AnyAsync(f => f.Id != booking.Id &&
                        f.TeremId == booking.TeremId &&
                        start < f.EndTime &&
                        end > f.StartTime, cancellationToken);
                var maintenanceConflict = !conflict && await KarbantartasiIdosavok.AnyAsync(m =>
                    m.TeremId == booking.TeremId &&
                    start < m.EndTime &&
                    end > m.StartTime, cancellationToken);
                if (conflict || maintenanceConflict)
                {
                    throw new InvalidOperationException(
                        conflict
                            ? "Booking for the room overlaps with an existing booking."
                            : "Booking overlaps with a maintenance period.");
                }
            }
        }
    }

    public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                value => Services.AppTime.ToUtc(value),
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
        {
        }
    }
}
