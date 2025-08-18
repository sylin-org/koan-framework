using Microsoft.Extensions.Options;
using Sora.Core;

namespace Sora.Data.Json;

internal sealed class JsonHealthContributor(IOptions<JsonDataOptions> options) : IHealthContributor
{
    public string Name => "data:json";
    public bool IsCritical => true;

    public Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        var path = options.Value.DirectoryPath;
        var data = new Dictionary<string, object?> { ["path"] = path };

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult(new HealthReport(Name, HealthState.Degraded, "DirectoryPath is not configured", null, data));
            }

            if (!Directory.Exists(path))
            {
                return Task.FromResult(new HealthReport(Name, HealthState.Unhealthy, "Data directory does not exist", null, data));
            }

            // Write test: create and delete a small temp file to verify write access
            var probe = Path.Combine(path, ".__healthcheck.json.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            if (File.Exists(probe)) File.Delete(probe);

            return Task.FromResult(new HealthReport(Name, HealthState.Healthy, null, null, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex, data));
        }
    }
}
