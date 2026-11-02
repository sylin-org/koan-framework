using Microsoft.Data.Sqlite;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>ARCH-0142: a worker that starts with the host finds its tables, so nothing changes the schema after the host reports started, which is what a new connection's first use
/// of an FTS5 table cannot survive.</summary>
public sealed class SchemaSettledSpec
{
    [Fact]
    public async Task The_job_worker_changes_no_schema_after_the_host_reports_started()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-jobs-settled-{Guid.NewGuid():n}.db");
        try
        {
            await using var host = await JobsHarness.StartSqliteAtAsync(path, clearOnStart: false, ownsDb: false);
            var atStart = SchemaVersion(path);
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            SchemaVersion(path).Should().Be(atStart);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    private static long SchemaVersion(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA schema_version";
        return (long)command.ExecuteScalar()!;
    }
}
