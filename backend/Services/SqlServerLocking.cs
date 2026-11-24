using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using ProjektLabor.Data;

namespace ProjektLabor.Services;

/// <summary>
/// Az alkalmazászár (sp_getapplock) nem szerezhető meg időben, mert egy párhuzamos művelet tartja.
/// </summary>
public sealed class DatabaseLockTimeoutException(string message) : Exception(message);

public static class SqlServerLocking
{
    public const int MaxAttempts = 3;

    // SQL Server hibakódok: 1205 = holtpont (deadlock) áldozata, 1222 = zárkérés időtúllépése.
    private static readonly HashSet<int> TransientConcurrencyErrorNumbers = [1205, 1222];

    // SQL Server hibakódok: 2601/2627 = egyedi index vagy egyediségi megszorítás sérül.
    private static readonly HashSet<int> UniqueViolationErrorNumbers = [2601, 2627];

    /// <summary>
    /// Lefuttatja a (saját tranzakciót nyitó) műveletet; holtpont vagy zár-időtúllépés esetén a
    /// félbemaradt változásokat eldobja, és rövid várakozás után legfeljebb <see cref="MaxAttempts"/>-szor
    /// újrapróbálja. A műveletnek minden adatot a saját tranzakcióján belül kell újraolvasnia.
    /// </summary>
    public static async Task<T> ExecuteWithRetryAsync<T>(AppDbContext db, Func<Task<T>> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            var trackedBefore = db.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
            try
            {
                return await db.Database.CreateExecutionStrategy().ExecuteAsync(operation);
            }
            catch (Exception exception) when (attempt < MaxAttempts && IsTransientConcurrencyFailure(exception))
            {
                DiscardChangesSince(db, trackedBefore);
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(50, 150) * attempt));
            }
        }
    }

    /// <summary>
    /// Holtpont, zár-időtúllépés vagy meg nem szerezhető alkalmazászár: a művelet később megismételhető.
    /// </summary>
    public static bool IsTransientConcurrencyFailure(Exception exception) =>
        exception is DatabaseLockTimeoutException ||
        FindSqlErrorNumbers(exception).Any(TransientConcurrencyErrorNumbers.Contains);

    /// <summary>
    /// Egyedi index sérült (pl. két párhuzamos kérés ugyanazt a felhasználónevet vagy kulcsszámot rögzítené).
    /// </summary>
    public static bool IsUniqueConstraintViolation(Exception exception) =>
        FindSqlErrorNumbers(exception).Any(UniqueViolationErrorNumbers.Contains);

    public static async Task AcquireAsync(
        AppDbContext db,
        string resource,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return;
        }

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 10000;
            SELECT @result;
            """;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new DatabaseLockTimeoutException($"Could not acquire SQL Server application lock '{resource}' (result {result}).");
        }
    }

    private static IEnumerable<int> FindSqlErrorNumbers(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                foreach (SqlError error in sqlException.Errors)
                {
                    yield return error.Number;
                }
            }
        }
    }

    // A sikertelen próbálkozás során betöltött vagy hozzáadott entitásokat leválasztjuk (az újrapróbálás
    // friss adatot olvasson), a korábban is követett entitások módosításait pedig visszaállítjuk.
    private static void DiscardChangesSince(AppDbContext db, HashSet<object> trackedBefore)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (!trackedBefore.Contains(entry.Entity) || entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }
    }
}
