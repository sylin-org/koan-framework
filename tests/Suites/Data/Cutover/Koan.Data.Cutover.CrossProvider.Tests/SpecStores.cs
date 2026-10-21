using MongoDB.Driver;
using Npgsql;

namespace Koan.Data.Cutover.CrossProvider.Tests;

/// <summary>
/// The live stores a spec of this lane writes, removed whole before and after it runs. Each spec owns its own PostgreSQL
/// schema and MongoDB database, so one spec's containers are never another's cutover target (a cutover refuses a target
/// that holds any user container, empty or not), and a rerun on a used database starts from nothing.
/// </summary>
internal static class SpecStores
{
    public static async Task Drop(CrossDatabaseFixture databases, string postgresSchema, string mongoDatabase)
    {
        await using (var connection = new NpgsqlConnection(databases.PostgresConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{postgresSchema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await new MongoClient(databases.MongoConnectionString).DropDatabaseAsync(mongoDatabase).ConfigureAwait(false);
    }
}
