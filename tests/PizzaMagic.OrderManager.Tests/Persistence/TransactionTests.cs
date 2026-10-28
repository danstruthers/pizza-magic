using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PizzaMagic.OrderManager.Tests.Persistence;

public class TransactionTests
{
    // Plan section 2.11: an EF transaction on SQLite takes the writer lock immediately (BEGIN IMMEDIATE), so the
    // idempotency re-check inside it cannot race another writer.
    [Fact]
    public async Task Transaction_is_immediate()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        await using var holder = database.NewContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();

        // A second writer (1 s busy timeout; 0 would mean "wait forever") cannot even begin while the first
        // transaction is open, although the first has not written anything yet.
        await using var other = new SqliteConnection(database.ConnectionString.Replace("Default Timeout=5", "Default Timeout=1"));
        await other.OpenAsync();
        var error = Should.Throw<SqliteException>(() => other.BeginTransaction());
        error.SqliteErrorCode.ShouldBe(5);   // SQLITE_BUSY
    }
}
