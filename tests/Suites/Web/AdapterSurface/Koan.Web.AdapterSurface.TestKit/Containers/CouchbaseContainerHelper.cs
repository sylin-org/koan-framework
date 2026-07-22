using System.Net.Http.Headers;
using System.Text;
using Koan.Testing.Containers;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class CouchbaseContainerHelper : KoanWebContainerHelper<CouchbaseFixture>
{
    public string ManagementUrl => Fixture.ManagementUrl;
    public string Bucket => Fixture.Bucket;
    public string Username => Fixture.AdminUser;
    public string Password => Fixture.AdminPassword;

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
            var queryBase = Fixture.QueryUrl;
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

}
