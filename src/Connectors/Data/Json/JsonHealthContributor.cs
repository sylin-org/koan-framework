using Koan.Core.Observability.Health;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Json.Runtime;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Json;

internal sealed class JsonHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IConfiguration _configuration;
    private readonly DataSourceRegistry _sources;
    private readonly IOptions<JsonDataOptions> _options;
    private readonly IAdapterFactory _owner;

    public JsonHealthContributor(
        IServiceProvider services,
        IConfiguration configuration,
        DataSourceRegistry sources,
        IDataDiagnostics diagnostics,
        IOptions<JsonDataOptions> options,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(Infrastructure.Constants.Provider.Name, services, diagnostics, defaultProvider)
    {
        _configuration = configuration;
        _sources = sources;
        _options = options;
        _owner = providers.Find(Infrastructure.Constants.Provider.Name)
            ?? throw new InvalidOperationException("The JSON provider is absent from the host Data catalog.");
    }

    protected override Task ProbeSource(string source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var route = JsonRoute.Resolve(_configuration, _sources, _options.Value, _owner, source);
        if (route.Policy.StorageLifecycle != StorageLifecycle.Managed ||
            route.Policy.Access != DataSourceAccess.ReadWrite)
        {
            if (!Directory.Exists(route.DirectoryPath))
                throw new DirectoryNotFoundException(
                    $"JSON source '{source}' requires existing directory '{route.DirectoryPath}' for " +
                    $"{route.Policy.StorageLifecycle}/{route.Policy.Access}.");
            _ = Directory.EnumerateFileSystemEntries(route.DirectoryPath).Take(1).ToArray();
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(route.DirectoryPath);
        var probe = Path.Combine(route.DirectoryPath, $".__koan-health-{Guid.CreateVersion7():N}.tmp");
        using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
        if (File.Exists(probe)) File.Delete(probe);
        return Task.CompletedTask;
    }
}
