using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string AdminUser = "Administrator";
    private const string AdminPassword = "password";
    private const int ManagementPort = 8091;

    private CouchbaseContainer? _container;
    private string _bucket = "";

    public override string Engine => "couchbase";
    protected override string Adapter => "couchbase";

    /// <summary>The auto-created bucket every spec in the assembly targets (resolved after start).</summary>
    public string Bucket => _bucket;

    protected override async Task<string> StartContainerAsync()
    {
        _container = new CouchbaseBuilder("couchbase:community-7.6.1").Build();
        await _container.StartAsync().ConfigureAwait(false);
        _bucket = _container.Buckets.First().Name;

        // Couchbase Community Edition only supports the 'forestdb' GSI storage mode, and it MUST be set before
        // any index is created — otherwise the adapter's CREATE PRIMARY INDEX fails with "Please Set Indexer
        // Storage Mode Before Create Index". The official Testcontainers module targets Enterprise defaults and
        // does not set it, so we set it here (the old hand-rolled fixture did the same during cluster init).
        await SetGsiStorageModeForestDbAsync().ConfigureAwait(false);

        return _container.GetConnectionString();
    }

    private async Task SetGsiStorageModeForestDbAsync()
    {
        var host = _container!.Hostname;
        var port = _container.GetMappedPublicPort(ManagementPort);
        using var http = new HttpClient { BaseAddress = new Uri($"http://{host}:{port}") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{AdminUser}:{AdminPassword}")));
        using var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("storageMode", "forestdb"),
        });
        var response = await http.PostAsync("/settings/indexes", body).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) => new[]
    {
        new KeyValuePair<string, string?>("Koan:Data:Sources:Default:Database", _bucket),
        new KeyValuePair<string, string?>("Koan:Data:Couchbase:ConnectionString", connectionString),
        new KeyValuePair<string, string?>("Koan:Data:Couchbase:Bucket", _bucket),
        new KeyValuePair<string, string?>("Koan:Data:Couchbase:Username", AdminUser),
        new KeyValuePair<string, string?>("Koan:Data:Couchbase:Password", AdminPassword),
    };
}
