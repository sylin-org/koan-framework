using Testcontainers.Couchbase;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class CouchbaseContainerHelper : IAsyncDisposable
{
    private CouchbaseContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }
    public string? ManagementUrl { get; private set; }
    public string Bucket { get; private set; } = "default";
    public string Username { get; private set; } = "Administrator";
    public string Password { get; private set; } = "password";

    public async Task InitializeAsync()
    {
        var explicitConn = Environment.GetEnvironmentVariable("Koan_TESTS_COUCHBASE")
                          ?? Environment.GetEnvironmentVariable("COUCHBASE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConn))
        {
            ConnectionString = explicitConn;
            ManagementUrl = Environment.GetEnvironmentVariable("Koan_TESTS_COUCHBASE_MGMT");
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new CouchbaseBuilder().Build();
            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = _container.GetConnectionString();
            // CouchbaseContainer maps management port 8091 to a random host port — surface it
            // explicitly so CouchbaseOptions.ManagementUrl can be passed to the cluster provider.
            var mgmtPort = _container.GetMappedPublicPort(8091);
            ManagementUrl = $"http://127.0.0.1:{mgmtPort}";
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Couchbase: {ex.GetType().Name}: {ex.Message}";
        }
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
