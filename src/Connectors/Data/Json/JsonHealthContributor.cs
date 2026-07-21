using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core.Observability.Health;
using Koan.Data.Connector.Json.Infrastructure;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using Koan.Data.Abstractions;

namespace Koan.Data.Connector.Json;

internal sealed class JsonHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IConfiguration _configuration;
    private readonly DataSourceRegistry _sourceRegistry;
    private readonly IOptions<JsonDataOptions> _options;
    private readonly IAdapterFactory _sourceOwner;

    public JsonHealthContributor(
        IServiceProvider services,
        IConfiguration configuration,
        DataSourceRegistry sourceRegistry,
        IDataDiagnostics diagnostics,
        IOptions<JsonDataOptions> options,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(Constants.Provider.Name, services, diagnostics, defaultProvider)
    {
        _configuration = configuration;
        _sourceRegistry = sourceRegistry;
        _options = options;
        _sourceOwner = providers.Find(Constants.Provider.Name)
            ?? throw new InvalidOperationException("The JSON provider is absent from the host Data catalog.");
    }

    protected override Task ProbeSource(string source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = AdapterConnectionResolver.GetSourceSetting(
            _configuration,
            _sourceRegistry,
            Constants.Provider.Name,
            source,
            Constants.Configuration.Keys.DirectoryPath,
            _options.Value.DirectoryPath,
            _sourceOwner);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"JSON directory is not configured for source '{source}'.");
        }

        // JsonRepository provisions its directory on first use. Readiness exercises that same
        // contract so a fresh, selected JSON store is ready without manual scaffolding.
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, $".__koan-health-{Guid.NewGuid():N}.tmp");
        using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
        if (File.Exists(probe)) File.Delete(probe);
        return Task.CompletedTask;
    }
}

