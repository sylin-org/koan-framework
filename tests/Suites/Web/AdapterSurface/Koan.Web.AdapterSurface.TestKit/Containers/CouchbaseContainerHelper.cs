using System.Net.Http.Headers;
using System.Text;
using Testcontainers.Couchbase;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class CouchbaseContainerHelper : IAsyncDisposable
{
    private CouchbaseContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }
    public string? ManagementUrl { get; private set; }
    // CouchbaseBuilder.Default bucket name is generated at runtime (a GUID), so the real value
    // gets pulled from _container.Buckets after StartAsync.
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
            Bucket = Environment.GetEnvironmentVariable("Koan_TESTS_COUCHBASE_BUCKET") ?? Bucket;
            IsAvailable = true;
            return;
        }

        try
        {
            // Pin to the rolling community image — 7.0.2 (the Testcontainers default) doesn't
            // fully support collection-level CREATE PRIMARY INDEX in N1QL ("syntax error - at .").
            // The rolling tag tracks a 7.6+ release that accepts the 3-part keyspace path.
            _container = new CouchbaseBuilder("couchbase:community").Build();
            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = _container.GetConnectionString();
            // CouchbaseContainer maps management port 8091 to a random host port — surface it
            // explicitly so CouchbaseOptions.ManagementUrl can be passed to the cluster provider.
            var mgmtPort = _container.GetMappedPublicPort(8091);
            ManagementUrl = $"http://127.0.0.1:{mgmtPort}";
            try
            {
                var queryPort = _container.GetMappedPublicPort(8093);
                _queryBaseUrl = $"http://127.0.0.1:{queryPort}";
            }
            catch { /* 8093 not exposed — fall back to management URL */ }
            // CouchbaseBuilder.Default.Name is a runtime GUID — read it back from the container.
            var firstBucket = _container.Buckets?.FirstOrDefault();
            if (firstBucket is not null)
            {
                Bucket = firstBucket.Name;
            }
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Couchbase: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task ResetAsync()
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(ManagementUrl)) return;
        // Bucket flush rolls back the primary index and breaks every subsequent N1QL query
        // (Indexer rollback ...). DELETE FROM via the N1QL REST endpoint is heavier per row but
        // preserves indexes. Find the N1QL port (8093) on the same host and submit the query.
        try
        {
            // ManagementUrl is "http://127.0.0.1:<8091mapped>". The query service typically uses
            // 8093 but in Testcontainers it's also mapped. Issue the query via the cluster
            // management API's POST /pools/default/buckets/{bucket}/controller endpoint is not
            // available for N1QL; we use the query service URL composed from the same scheme.
            // For Testcontainers, we read the mapped 8093 port if available; else fall back to
            // the management URL host. The standard query endpoint accepts POST application/json
            // with {"statement": "..."}.
            using var client = new HttpClient();
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

            // Use the query port if we know it (Testcontainers maps 8093). Otherwise reuse the
            // management base — most cloud-managed deployments expose a unified endpoint.
            var queryBase = _queryBaseUrl ?? ManagementUrl;
            foreach (var collection in new[] { "_default", "widgets_surface" })
            {
                try
                {
                    var stmt = $"DELETE FROM `{Bucket}`.`_default`.`{collection}`";
                    var payload = new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(new { statement = stmt, scan_consistency = "request_plus" }),
                        Encoding.UTF8, "application/json");
                    await client.PostAsync($"{queryBase}/query/service", payload).ConfigureAwait(false);
                }
                catch { /* collection may not exist yet */ }
            }
        }
        catch { /* best effort */ }
    }

    private string? _queryBaseUrl;

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            try { await _container.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }
}
