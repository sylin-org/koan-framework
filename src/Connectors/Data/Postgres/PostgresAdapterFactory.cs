using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Connector.Postgres.Infrastructure;
using Koan.Data.Connector.Postgres.Runtime;
using Koan.Data.Relational;
using Koan.Data.Relational.Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Koan.Data.Connector.Postgres;

[ProviderPriority(14)]
[KoanService(ServiceKind.Database, shortCode: Constants.Provider, name: "PostgreSQL",
    ContainerImage = "postgres", DefaultTag = "18.4", DefaultPorts = [5432],
    Capabilities = ["protocol=postgres"],
    Env = ["POSTGRES_USER=postgres", "POSTGRES_PASSWORD", "POSTGRES_DB=Koan"],
    Volumes = ["./Data/postgres-18:/var/lib/postgresql"],
    AppEnv = ["Koan__Data__Postgres__ConnectionString={scheme}://{host}:{port}", "Koan__Data__Postgres__Database=Koan"],
    Scheme = "postgres", Host = "postgres", EndpointPort = 5432, UriPattern = "postgres://{host}:{port}",
    LocalScheme = "postgres", LocalHost = "localhost", LocalPort = 5432, LocalPattern = "postgres://{host}:{port}")]
public sealed class PostgresAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory, IDataAdapterSetup
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => ["postgresql", "npgsql"];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Postgres"];

    public void DescribeClaims(IDataClaims claims) => NpgsqlFeatures.Declare(claims, lexical: true);

    public DataAdapterSetupDescriptor DescribeSetup() => new("PostgreSQL",
    [
        new(nameof(PostgresOptions.ConnectionString), "Connection string", DataProviderSetupFieldKind.ConnectionString,
            Placeholder: "Host=localhost;Database=Koan;Username=postgres;Password=...")
    ]);

    public async Task<DataProviderProbeResult> Probe(
        IServiceProvider services, DataProviderProbeContext candidate, CancellationToken ct = default)
    {
        var connectionString = candidate.Require(nameof(PostgresOptions.ConnectionString));
        if (connectionString.Equals("auto", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PostgreSQL candidate setup requires a concrete connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return DataProviderProbeResult.Ready("The PostgreSQL target answered a read-only query.");
    }

    public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords,
        ["sql"],
        enforcesReadLanes: true);

    public IDataSourceIntegration CreateSource(IServiceProvider services, string source)
    {
        var route = ResolveRoute(services, source);
        return new RelationalSourceIntegration(
            lane => new NpgsqlConnection(route.ReadLanes[lane]),
            route.ReadLanes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            static async (connection, ct) =>
            {
                var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
                try
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "SET TRANSACTION READ ONLY";
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    return transaction;
                }
                catch
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            },
            new PostgresInspector(route));
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider services, string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        return new NpgsqlRepository<TEntity, TKey>(services, new NpgsqlRepositoryOptions
        {
            ProviderName = Provider,
            ConnectionString = route.ConnectionString,
            Source = route.Source,
            SearchPath = route.SearchPath,
            NamingStyle = route.Options.NamingStyle,
            Separator = route.Options.Separator,
            DdlPolicy = route.Options.DdlPolicy,
            SchemaMatching = route.Options.SchemaMatching,
            AllowProductionDdl = route.Options.AllowProductionDdl,
            SourcePlan = route.Plan,
            SupportsLexicalIndex = true
        }, services.GetRequiredService<IStorageNameResolver>(), this);
    }

    /// <summary>
    /// Server, port, database and search path: the parts that place a table. Credentials and pool settings do not,
    /// so two spellings of one database compare equal. An unparsable connection falls back to its plan identity.
    /// </summary>
    public string? ResolveStoreIdentity(IServiceProvider services, string source)
    {
        var route = ResolveRoute(services, source);
        var schema = (route.SearchPath ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        string server;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(route.ConnectionString);
            var database = string.IsNullOrWhiteSpace(builder.Database) ? builder.Username : builder.Database;
            server = $"{builder.Host?.ToLowerInvariant()}|{builder.Port}|{database?.ToLowerInvariant()}";
        }
        catch (ArgumentException)
        {
            server = route.Plan.ConnectionIdentity;
        }
        return DataSourceRegistry.IdentityOf($"{server}|{schema}");
    }

    /// <summary>
    /// Asks the servers, through an advisory lock keyed by database, whether the two routes reach one database
    /// (<see cref="NpgsqlStoreProof"/>). Different databases, or one database under different schemas, are proven apart.
    /// </summary>
    public ValueTask<bool> ProveDistinctStores(
        IServiceProvider services,
        string source,
        string other,
        CancellationToken ct = default)
    {
        var first = ResolveRoute(services, source);
        var second = ResolveRoute(services, other);
        return NpgsqlStoreProof.Distinct(first.ConnectionString, first.SearchPath, second.ConnectionString, second.SearchPath, ct);
    }

    /// <summary>
    /// The id in the reserved <c>__koan_store</c> table of the route's schema (DATA-0148). It is created only where the
    /// source may create a table (Managed + ReadWrite, <c>DdlPolicy=AutoCreate</c>, the production DDL gate); otherwise
    /// it is read.
    /// </summary>
    public ValueTask<string?> ReadStoreMarker(
        IServiceProvider services,
        string source,
        bool create,
        CancellationToken ct = default)
    {
        var route = ResolveRoute(services, source);
        var allowed = create && Koan.Data.Relational.Orchestration.RelationalDdlGate.MayCreate(
            route.Plan, route.Options.DdlPolicy, route.Options.AllowProductionDdl);
        return NpgsqlStoreMarker.Read(route.ConnectionString, route.SearchPath, allowed, ct);
    }

    internal PostgresRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var options = services.GetRequiredService<IOptions<PostgresOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, options.ConnectionString, this);
        var searchPath = AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, resolvedSource, "SearchPath", options.SearchPath, this);
        var readLanes = registry.GetSource(resolvedSource)?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString))
            .ToDictionary(
                static lane => lane.Key,
                static lane => lane.Value.ConnectionString,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new PostgresRoute(
            resolvedSource,
            connection,
            searchPath,
            options,
            registry.GetPlan(resolvedSource, Provider, connection),
            readLanes);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<PostgresOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true },
            MaxIdentifierBytes = 63
        };
    }
}

internal sealed record PostgresRoute(
    string Source,
    string ConnectionString,
    string SearchPath,
    PostgresOptions Options,
    DataSourcePlan Plan,
    IReadOnlyDictionary<string, string> ReadLanes);
