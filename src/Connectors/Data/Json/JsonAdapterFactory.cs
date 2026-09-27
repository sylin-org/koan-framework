using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Json.Runtime;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Json;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
public sealed class JsonAdapterFactory : IDataAdapterFactory, IDataAdapterSetup
{
    public string Provider => Infrastructure.Constants.Provider.Name;
    public bool IsAutomaticFloor => true;
    public IReadOnlyCollection<string> ReferenceIdentities =>
        [Infrastructure.Constants.Provider.ReferenceIdentity];

    public void DescribeClaims(IDataClaims claims) => JsonFeatures.Declare(claims);

    public DataAdapterSetupDescriptor DescribeSetup() => new("JSON files",
    [
        new(nameof(JsonDataOptions.DirectoryPath), "Directory", DataProviderSetupFieldKind.Directory,
            DefaultValue: new JsonDataOptions().DirectoryPath, Placeholder: "data")
    ]);

    public async Task<DataProviderProbeResult> Probe(
        IServiceProvider services,
        DataProviderProbeContext candidate,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var directory = Path.GetFullPath(candidate.Require(nameof(JsonDataOptions.DirectoryPath)));
        if (!Directory.Exists(directory))
        {
            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                throw new DirectoryNotFoundException();
            _ = Directory.EnumerateFileSystemEntries(parent).Take(1).ToArray();
            return DataProviderProbeResult.Reachable(
                "The JSON directory can be provisioned beneath an existing parent directory.");
        }

        _ = Directory.EnumerateFileSystemEntries(directory).Take(1).ToArray();
        var probe = Path.Combine(directory, $".__koan-setup-{Guid.CreateVersion7():N}.tmp");
        await using (var stream = new FileStream(
                         probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                         FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            await stream.FlushAsync(ct).ConfigureAwait(false);
        if (File.Exists(probe)) File.Delete(probe);
        return DataProviderProbeResult.Ready("The JSON directory is readable and writable.");
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = Infrastructure.Constants.Provider.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var resolvedSource = string.IsNullOrWhiteSpace(source)
            ? Infrastructure.Constants.Provider.DefaultSource
            : source;
        if (services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(resolvedSource) is not null)
        {
            throw new NotSupportedException(
                $"JSON does not expose a physical compatibility-mapping surface for '{typeof(TEntity).Name}'. " +
                "Remove Map<T>(...) or route the source to an adapter that supports physical mappings.");
        }

        var route = JsonRoute.Resolve(
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<DataSourceRegistry>(),
            services.GetRequiredService<IOptions<JsonDataOptions>>().Value,
            this,
            resolvedSource);

        return route.Layout switch
        {
            JsonStorageLayout.Aggregate => new JsonRepository<TEntity, TKey>(
                route,
                services.GetRequiredService<JsonFileRegistry>(),
                services.GetRequiredService<Koan.Data.Core.Semantics.DataSegmentationPlan>(),
                this,
                services),
            JsonStorageLayout.IndividualFiles => new JsonIndividualFilesRepository<TEntity, TKey>(
                route,
                services.GetRequiredService<JsonIndividualFileRegistry>(),
                services.GetRequiredService<Koan.Data.Core.Semantics.DataSegmentationPlan>(),
                this,
                services),
            _ => throw new InvalidOperationException(
                $"JSON layout '{route.Layout}' is not supported for source '{resolvedSource}'.")
        };
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.AsIs,
        PartitionSeparator = Infrastructure.Constants.Storage.PartitionSeparator,
        Partition = PartitionTokenPolicy.Default
    };
}
