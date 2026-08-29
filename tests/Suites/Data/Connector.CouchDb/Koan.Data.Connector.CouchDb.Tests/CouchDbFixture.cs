using DotNet.Testcontainers.Builders;

namespace Koan.Data.Connector.CouchDb.Tests;

/// <summary>
/// CouchDB 3.5 container fixture, built on the generic Testcontainers builder (the repo carries no
/// CouchDB module package and the adapter is plain HTTP). The image HAS a healthcheck but the wait is
/// pinned to the <c>/_up</c> endpoint regardless, so a server that binds without being ready fails
/// fast instead of serving 503s into the first specs. The admin party is disabled in 3.x; credentials
/// ride the fixture's settings into the adapter's options.
/// </summary>
public sealed class CouchDbFixture : KoanContainerFixture
{
    private const string Image = "couchdb:3.5";
    private const string User = "koan";
    private const string Password = "koan";

    private DotNet.Testcontainers.Containers.IContainer? _container;

    public override string Engine => "couchdb";
    protected override string Adapter => "couchdb";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new ContainerBuilder(Image)
            .WithImage(Image)
            .WithPortBinding(5984, true)
            .WithEnvironment("COUCHDB_USER", User)
            .WithEnvironment("COUCHDB_PASSWORD", Password)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request
                    .ForPort(5984)
                    .ForPath("/_up")))
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        var port = _container.GetMappedPublicPort(5984);
        return $"http://localhost:{port}";
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) =>
    [
        new("Koan:Data:CouchDb:Endpoint", connectionString),
        new("Koan:Data:CouchDb:UserId", User),
        new("Koan:Data:CouchDb:Password", Password),
        new("Koan:Data:CouchDb:Database", "koan"),
        new("Koan:Data:CouchDb:Readiness:EnableReadinessGating", "false"),
    ];
}
