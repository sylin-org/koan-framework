using System.Collections.Generic;
using System.Threading.Tasks;
using Testcontainers.MongoDb;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 Mongo container fixture. Starts the official <see cref="MongoDbContainer"/> module and
/// hands its connection string + a fixed database to the Koan data layer. Per-test partitions
/// (collection-prefix isolation) replace the old per-run database, so a single <c>koan</c> database is
/// shared across the assembly's specs. Wire-shape specs build their own driver client from
/// <see cref="KoanContainerFixture.ConnectionString"/> + <see cref="Database"/>.
/// </summary>
public sealed class MongoFixture : KoanContainerFixture
{
    private MongoDbContainer? _container;

    public override string Engine => "mongo";
    protected override string Adapter => "mongo";

    /// <summary>The shared Mongo database every spec in the assembly targets.</summary>
    public string Database => "koan";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new MongoDbBuilder("mongo:8.3.4").Build();
        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) => new[]
    {
        new KeyValuePair<string, string?>("Koan:Data:Sources:Default:Database", Database),
        new KeyValuePair<string, string?>("Koan:Data:Mongo:ConnectionString", connectionString),
        new KeyValuePair<string, string?>("Koan:Data:Mongo:Database", Database),
    };
}
