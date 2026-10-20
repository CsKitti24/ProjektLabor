using Microsoft.EntityFrameworkCore;
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Unique constraints for Felhasznalo
            modelBuilder.Entity<Felhasznalo>()
                .HasIndex(u => u.Username).IsUnique();
            modelBuilder.Entity<Felhasznalo>()
                .HasIndex(u => u.Email).IsUnique();

            // Many-to-many between Kulcs (master keys) and Terem (rooms) for access rights
            modelBuilder.Entity<KulcsTerem>()
                .HasKey(kt => new { kt.KulcsId, kt.TeremId });
            modelBuilder.Entity<KulcsTerem>()
                .HasOne(kt => kt.Kulcs)
                .WithMany(k => k.KulcsTeremKapcsok)
                .HasForeignKey(kt => kt.KulcsId);
            modelBuilder.Entity<KulcsTerem>()
                .HasOne(kt => kt.Terem)
                .WithMany(t => t.KulcsTeremKapcsok)
                .HasForeignKey(kt => kt.TeremId);

            // Seed default system parameters
            modelBuilder.Entity<Rendszerparameter>().HasData(
                new Rendszerparameter { Kulcs = "napi_kulcsleadasi_zarasi_idopont", Ertek = "22:00", Leiras = "Napi kulcsleadási zárási időpont", ModositasIdeje = DateTime.UtcNow },
                new Rendszerparameter { Kulcs = "no_show_idokorlat_perc", Ertek = "15", Leiras = "No-show időkorlát percben", ModositasIdeje = DateTime.UtcNow }
            );
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ValidateBookings();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async System.Threading.Tasks.Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, System.Threading.CancellationToken cancellationToken = default)
        {
            ValidateBookings();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ValidateBookings()
        {
            var overlapping = ChangeTracker.Entries<Foglalas>()
                .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added || e.State == Microsoft.EntityFrameworkCore.EntityState.Modified)
                .Select(e => e.Entity);
            foreach (var booking in overlapping)
            {
                var conflict = Foglalasok.Any(f => f.Id != booking.Id && f.TeremId == booking.TeremId &&
                    ((booking.StartTime < f.EndTime) && (booking.EndTime > f.StartTime)));
                if (conflict)
                {
                    throw new InvalidOperationException($"Booking for room {booking.TeremId} overlaps with an existing booking.");
                }
            }
        }
    }
}