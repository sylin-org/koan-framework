using MongoDB.Driver;
using Koan.Testing.Containers;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

/// <summary>
/// Adds a per-Web-suite database identity and reset operation to the public Mongo fixture.
/// </summary>
public sealed class MongoContainerHelper : KoanWebContainerHelper<MongoFixture>
{
    private string? _connectionString;

    public override string? ConnectionString => _connectionString;
    public string Database { get; } = $"koan_surface_{Guid.NewGuid():N}";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        _connectionString = EnsureDatabase(Fixture.ConnectionString, Database);
    }

    public async Task ResetAsync()
    {
        if (ConnectionString is null) return;
        var client = new MongoClient(ConnectionString);
        await client.DropDatabaseAsync(Database).ConfigureAwait(false);
    }

    private static string EnsureDatabase(string connectionString, string database)
    {
        var builder = new MongoUrlBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.Username) &&
            string.IsNullOrWhiteSpace(builder.AuthenticationSource))
        {
            builder.AuthenticationSource = string.IsNullOrWhiteSpace(builder.DatabaseName)
                ? "admin"
                : builder.DatabaseName;
        }

        builder.DatabaseName = database;
        return builder.ToString();
    }

}
