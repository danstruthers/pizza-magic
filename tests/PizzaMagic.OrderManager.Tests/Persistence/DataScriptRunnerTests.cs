using PizzaMagic.OrderManager.Data;

namespace PizzaMagic.OrderManager.Tests.Persistence;

public class DataScriptRunnerTests
{
    private static string CopyOfData()
    {
        var copy = Path.Combine(Path.GetTempPath(), "pizzamagic-tests", Guid.NewGuid().ToString("N"), "data");
        Directory.CreateDirectory(Path.Combine(copy, "seed"));
        foreach (var file in Directory.GetFiles(Path.Combine(TestDatabase.DataDirectory, "seed"), "*.sql"))
        {
            File.Copy(file, Path.Combine(copy, "seed", Path.GetFileName(file)));
        }
        return copy;
    }

    [Fact]
    public async Task Data_script_runs_once_same_hash_is_noop_changed_hash_refused()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        var data = CopyOfData();
        await using var db = database.NewContext();
        var runner = new DataScriptRunner(db, database.Clock);

        // Already applied by the fixture with identical content: every script is a no-op.
        var rerun = await runner.ApplySeedsAsync(data);
        rerun.ShouldBe([
            new DataScriptResult("seed/0000-time-zones.sql", DataScriptOutcome.AlreadyApplied),
            new DataScriptResult("seed/0001-core.sql", DataScriptOutcome.AlreadyApplied),
        ]);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM product")).ShouldBe(17);

        // A new script is applied once and logged.
        File.WriteAllText(Path.Combine(data, "seed", "0002-extra.sql"), "INSERT INTO order_channel (code, display_name) VALUES ('KIOSK', 'Kiosk');");
        (await runner.ApplySeedsAsync(data)).Last().ShouldBe(new DataScriptResult("seed/0002-extra.sql", DataScriptOutcome.Applied));
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM data_script_log")).ShouldBe(3);

        // Changing an applied script is refused before anything runs.
        File.AppendAllText(Path.Combine(data, "seed", "0002-extra.sql"), "\nINSERT INTO order_channel (code, display_name) VALUES ('APP', 'App');");
        var error = await Should.ThrowAsync<DataScriptChangedException>(() => runner.ApplySeedsAsync(data));
        error.Message.ShouldBe("seed/0002-extra.sql was already applied with different content; put the change in a new file.");
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM order_channel")).ShouldBe(4);
    }

    [Fact]
    public async Task A_failing_script_rolls_back_and_is_not_logged()
    {
        await using var database = await TestDatabase.CreateSeededAsync();
        var data = CopyOfData();
        File.WriteAllText(Path.Combine(data, "seed", "0002-broken.sql"),
            "INSERT INTO order_channel (code, display_name) VALUES ('KIOSK', 'Kiosk'); INSERT INTO no_such_table VALUES (1);");
        await using var db = database.NewContext();

        await Should.ThrowAsync<Exception>(() => new DataScriptRunner(db, database.Clock).ApplySeedsAsync(data));

        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM order_channel WHERE code = 'KIOSK'")).ShouldBe(0);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM data_script_log")).ShouldBe(2);
    }
}
