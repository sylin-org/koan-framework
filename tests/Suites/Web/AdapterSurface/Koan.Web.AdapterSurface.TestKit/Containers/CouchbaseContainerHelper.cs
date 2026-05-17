using Testcontainers.Couchbase;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class CouchbaseContainerHelper : IAsyncDisposable
{
    private CouchbaseContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }
    public string Bucket { get; private set; } = "koan_surface";
    public string Username { get; private set; } = "Administrator";
    public string Password { get; private set; } = "password";

    public async Task InitializeAsync()
    {
        var explicitConn = Environment.GetEnvironmentVariable("Koan_TESTS_COUCHBASE")
                          ?? Environment.GetEnvironmentVariable("COUCHBASE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConn))
        {
            ConnectionString = explicitConn;
            IsAvailable = true;
            return;
        }

        UnavailableReason =
            "Couchbase containerised tests are skipped: Testcontainers.Couchbase exposes the KV and management ports " +
            "on separate random host ports, but Koan's CouchbaseClusterProvider derives the management URL from the " +
            "single KV connection string. Set Koan_TESTS_COUCHBASE to an externally provisioned cluster connection " +
            "string to run this adapter's surface specs.";
        await Task.CompletedTask;
    }

    public Task ResetAsync() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            try { await _container.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }
}
