using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.Cutover.CrossProvider.Tests;

public sealed class CrossDatabaseFixture : IAsyncLifetime
{
    private readonly MongoFixture _mongo = new();
    private readonly PostgresFixture _postgres = new();

    public string MongoConnectionString => _mongo.ConnectionString;
    public string MongoDatabase => "koan_cutover";
    public string PostgresConnectionString => _postgres.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _mongo.InitializeAsync().ConfigureAwait(false);
        try
        {
            await _postgres.InitializeAsync().ConfigureAwait(false);
        }
        catch
        {
            await _mongo.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _postgres.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await _mongo.DisposeAsync().ConfigureAwait(false);
        }
    }
}
