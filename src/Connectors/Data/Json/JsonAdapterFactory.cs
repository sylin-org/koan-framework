using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
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

    /// <summary>
    /// The source's resolved directory, upper-cased because two spellings of one path meet on a case-insensitive
    /// file system. Both layouts write beneath it, so the directory is the store.
    /// </summary>
    public string? ResolveStoreIdentity(IServiceProvider services, string source)
    {
        var route = JsonRoute.Resolve(
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<DataSourceRegistry>(),
            services.GetRequiredService<IOptions<JsonDataOptions>>().Value,
            this,
            string.IsNullOrWhiteSpace(source) ? Infrastructure.Constants.Provider.DefaultSource : source);
        return DataSourceRegistry.IdentityOf("json\n" + route.DirectoryPath.ToUpperInvariant());
    }

    /// <summary>
    /// A directory that does not exist yet cannot be one that does. Two existing directories are not proven apart: a
    /// link, a junction or a path prefix can name one directory two ways, and telling them apart would mean writing a
    /// marker into the store (NK-305, DATA-0143).
    /// </summary>
    public ValueTask<bool> ProveDistinctStores(
        IServiceProvider services,
        string source,
        string other,
        CancellationToken ct = default)
    {
        string Directory(string name) => JsonRoute.Resolve(
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<DataSourceRegistry>(),
            services.GetRequiredService<IOptions<JsonDataOptions>>().Value,
            this,
            string.IsNullOrWhiteSpace(name) ? Infrastructure.Constants.Provider.DefaultSource : name).DirectoryPath;
        return ValueTask.FromResult(!System.IO.Directory.Exists(Directory(source)) || !System.IO.Directory.Exists(Directory(other)));
    }

    /// <summary>
    /// The id in the reserved <c>__koan_store</c> file of the source's directory (DATA-0148). The file has no
    /// <c>.json</c> extension, so no layout reads it as an entity set. It is created only for a Managed + ReadWrite
    /// source; any other source reads it and never creates it or the directory.
    /// </summary>
    public async ValueTask<string?> ReadStoreMarker(
        IServiceProvider services,
        string source,
        bool create,
        CancellationToken ct = default)
    {
        var route = JsonRoute.Resolve(
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<DataSourceRegistry>(),
            services.GetRequiredService<IOptions<JsonDataOptions>>().Value,
            this,
            string.IsNullOrWhiteSpace(source) ? Infrastructure.Constants.Provider.DefaultSource : source);
        var path = Path.Combine(route.DirectoryPath, StoreMarker.Name);
        if (!File.Exists(path))
        {
            if (!create || route.Policy.StorageLifecycle != StorageLifecycle.Managed ||
                route.Policy.Access != DataSourceAccess.ReadWrite)
                return null;
            Directory.CreateDirectory(route.DirectoryPath);
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(StoreMarker.NewId().AsMemory(), ct).ConfigureAwait(false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another writer created it first; its id is the marker.
            }
        }

        var marker = (await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)).Trim();
        return marker.Length == 0 ? null : marker;
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.AsIs,
        PartitionSeparator = Infrastructure.Constants.Storage.PartitionSeparator,
        Partition = PartitionTokenPolicy.Default
    };
}
