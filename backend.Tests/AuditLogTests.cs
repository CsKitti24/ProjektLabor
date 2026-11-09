using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace backend.Tests;

public sealed class AuditLogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingAuditEntries_CannotBeUpdatedOrDeleted(bool deleteEntry)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AppendOnlyAuditTest_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var audit = new AuditLog
        {
            MuveletTipus = "TEST",
            EntitasTipus = "Test",
            EntitasId = "1"
        };
        db.AuditLogok.Add(audit);
        await db.SaveChangesAsync();

        if (deleteEntry)
        {
            db.AuditLogok.Remove(audit);
        }
        else
        {
            audit.MuveletTipus = "CHANGED";
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync());
        Assert.Contains("append-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
