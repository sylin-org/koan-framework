using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Testcontainers.Couchbase;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 Couchbase container fixture. Starts the official <see cref="CouchbaseContainer"/> module,
/// which boots a Community node, runs the Data/Index/Query/Search services, and auto-creates a default
/// bucket (with a primary index) on start — replacing the hundreds of lines of manual REST bootstrap +
/// alternate-address publishing in the old harness fixture. Heavy (~30s start). The module names the
/// default bucket with a per-run GUID, so the bucket name is read back from <see cref="CouchbaseContainer.Buckets"/>
/// after start and fed to the Koan adapter. The module's admin credentials are <c>Administrator</c>/<c>password</c>.
/// </summary>
public sealed class CouchbaseFixture : KoanContainerFixture
{
    private const string Admin = "Administrator";
    private const string AdminPass = "password";
    private const int ManagementPort = 8091;

    private CouchbaseContainer? _container;
    private string _bucket = "";
    private string _managementUrl = "";

    public override string Engine => "couchbase";
    protected override string Adapter => "couchbase";

    /// <summary>The auto-created bucket every spec in the assembly targets (resolved after start).</summary>
    public string Bucket => _bucket;

    /// <summary>The cluster's HTTP management endpoint (mapped host:port) — used to provision per-source buckets
    /// (the AODB Database conformance cell) and to point a routed Couchbase source at this server.</summary>
    public string ManagementUrl => _managementUrl;

    /// <summary>The container's admin credentials (the official Testcontainers Couchbase module's defaults).</summary>
    public string AdminUser => Admin;
    public string AdminPassword => AdminPass;

    protected override async Task<string> StartContainerAsync()
    {
        _container = new CouchbaseBuilder("couchbase:community-7.6.1").Build();
        await _container.StartAsync().ConfigureAwait(false);
        _bucket = _container.Buckets.First().Name;
        _managementUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ManagementPort)}";

        // Couchbase Community Edition only supports the 'forestdb' GSI storage mode, and it MUST be set before
        // any index is created — otherwise the adapter's CREATE PRIMARY INDEX fails with "Please Set Indexer
        // Storage Mode Before Create Index". The official Testcontainers module targets Enterprise defaults and
        // does not set it, so we set it here (the old hand-rolled fixture did the same during cluster init).
        await SetGsiStorageModeForestDbAsync().ConfigureAwait(false);

        return _container.GetConnectionString();
    }

    private HttpClient AdminHttp()
    {
        var http = new HttpClient { BaseAddress = new Uri(_managementUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Admin}:{AdminPass}")));
        return http;
    }

    private async Task SetGsiStorageModeForestDbAsync()
    {
        using var http = AdminHttp();
        using var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("storageMode", "forestdb") });
        var response = await http.PostAsync("/settings/indexes", body).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Create a Couchbase bucket on this cluster and wait for it to be accessible — the per-source physical store the
    /// AODB Database conformance cell routes to (Database mode = a distinct native bucket per source, the catalogue's
    /// 3-level mapping bucket=source · scope=partition · collection=entity). A small RAM quota keeps several buckets
    /// within the node's data-service quota; buckets are reclaimed with the container, so no teardown is needed.
    /// </summary>
    public async Task ProvisionBucketAsync(string name, int ramQuotaMb = 100)
    {
        // The Testcontainers Couchbase node provisions a small data-service quota (~256MB) — enough for the single
        // fixture bucket but not for the extra per-source buckets the Database conformance cell needs. Raise it first
        // (bounded by node RAM) so several small buckets fit.
        await EnsureDataQuotaAsync(384).ConfigureAwait(false);

        using var http = AdminHttp();
        using var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("name", name),
            new KeyValuePair<string, string>("bucketType", "couchbase"),
            new KeyValuePair<string, string>("ramQuotaMB", ramQuotaMb.ToString()),
            new KeyValuePair<string, string>("replicaNumber", "0"),
            new KeyValuePair<string, string>("flushEnabled", "0"),
        });
        var response = await http.PostAsync("/pools/default/buckets", body).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Accepted)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!(response.StatusCode == HttpStatusCode.BadRequest && error.Contains("already exists", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Failed to provision Couchbase bucket '{name}' (HTTP {(int)response.StatusCode}): {error}");
        }

        // Wait for the bucket to come up (healthy) before the adapter tries to open it.
        for (var i = 0; i < 30; i++)
        {
            try
            {
                var probe = await http.GetAsync($"/pools/default/buckets/{name}").ConfigureAwait(false);
                if (probe.IsSuccessStatusCode)
                {
                    var json = await probe.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (json.Contains("\"status\":\"healthy\"", StringComparison.OrdinalIgnoreCase) || json.Contains("\"healthy\":true", StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }
            catch { /* transient during bucket warm-up */ }
            await Task.Delay(1000).ConfigureAwait(false);
        }
    }

    // Raise the cluster's data-service memory quota to <paramref name="desiredDataMb"/> when it is lower, bounded so
    // the total service quotas stay within ~80% of node RAM (Couchbase rejects an over-commit). Best-effort: if the
    // node cannot grow the quota, a too-large bucket create surfaces the real error rather than this masking it.
    private async Task EnsureDataQuotaAsync(int desiredDataMb)
    {
        using var http = AdminHttp();
        var poolsResp = await http.GetAsync("/pools/default").ConfigureAwait(false);
        if (!poolsResp.IsSuccessStatusCode) return;
        using var doc = System.Text.Json.JsonDocument.Parse(await poolsResp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var root = doc.RootElement;

        var currentData = root.TryGetProperty("memoryQuota", out var mq) ? mq.GetInt32() : 256;
        if (currentData >= desiredDataMb) return;

        long nodeRamBytes = 0;
        if (root.TryGetProperty("nodes", out var nodes) && nodes.GetArrayLength() > 0 &&
            nodes[0].TryGetProperty("memoryTotal", out var mt))
            nodeRamBytes = mt.GetInt64();
        int Quota(string p) => root.TryGetProperty(p, out var v) ? v.GetInt32() : 0;
        var otherQuotas = Quota("indexMemoryQuota") + Quota("ftsMemoryQuota") + Quota("cbasMemoryQuota") + Quota("eventingMemoryQuota");
        var nodeRamMb = nodeRamBytes > 0 ? (int)(nodeRamBytes / (1024 * 1024)) : desiredDataMb + otherQuotas + 512;

        var maxData = (int)(nodeRamMb * 0.8) - otherQuotas;
        var target = Math.Min(desiredDataMb, maxData);
        if (target <= currentData) return;

        using var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("memoryQuota", target.ToString()) });
        await http.PostAsync("/pools/default", body).ConfigureAwait(false);
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) => new[]
    {
        new KeyValuePair<string, string?>("Koan:Data:Sources:Default:Database", _bucket),
        new KeyValuePair<string, string?>("Koan:Data:Couchbase:ConnectionString", connectionString),
        new KeyValuePair<string, string?>("Koan:Data:Couchbase:Bucket", _bucket),
        new KeyValuePair<string, string?>("Koan:Data:Couchbase:Username", Admin),
        new KeyValuePair<string, string?>("Koan:Data:Couchbase:Password", AdminPass),
    };
}
