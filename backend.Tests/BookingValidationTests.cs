using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using Xunit;

namespace backend.Tests
{
    public class BookingValidationTests
    {
        private AppDbContext CreateInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            return new AppDbContext(options);
        }

        [Fact]
        public async Task SaveChangesAsync_OverlappingBookingsForSameRoom_ThrowsInvalidOperationException()
        {
            // Arrange
            var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var baseTime = DateTime.UtcNow.Date.AddHours(10);

            var booking1 = new Foglalas
            {
                Id = 1,
                FelhasznaloId = 1,
                TeremId = 101,
                StartTime = baseTime,
                EndTime = baseTime.AddMinutes(45),
                Status = "Aktiv"
            };

            db.Foglalasok.Add(booking1);
            await db.SaveChangesAsync();

            var conflictingBooking = new Foglalas
            {
                Id = 2,
                FelhasznaloId = 2,
                TeremId = 101, // Same room
                StartTime = baseTime.AddMinutes(15), // Overlaps with booking1
                EndTime = baseTime.AddMinutes(60),
                Status = "Aktiv"
            };

            db.Foglalasok.Add(conflictingBooking);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("overlaps with an existing booking", ex.Message);
        }

        [Fact]
        public async Task SaveChangesAsync_NonOverlappingBookings_SavesSuccessfully()
        {
            // Arrange
            var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
            var baseTime = DateTime.UtcNow.Date.AddHours(10);

            var booking1 = new Foglalas
            {
                Id = 1,
                FelhasznaloId = 1,
                TeremId = 101,
                StartTime = baseTime,
                EndTime = baseTime.AddMinutes(45),
                Status = "Aktiv"
            };

            var booking2 = new Foglalas
            {
                Id = 2,
                FelhasznaloId = 2,
                TeremId = 101,
                StartTime = baseTime.AddMinutes(45), // Immediately after booking1
                EndTime = baseTime.AddMinutes(90),
                Status = "Aktiv"
            };

            db.Foglalasok.Add(booking1);
            db.Foglalasok.Add(booking2);

            // Act
            var count = await db.SaveChangesAsync();

            // Assert
            Assert.True(count > 0);
            Assert.Equal(2, await db.Foglalasok.CountAsync());
        }
    }
}
