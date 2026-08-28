using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational;
using DuckDB.NET.Data;

namespace Koan.Data.Connector.DuckDb.Runtime;

internal sealed class DuckDbInspector(DuckDbRoute route, DuckDbConnections connections) :
    IDataSourceInspectorAdapter,
    IDataSourceStatusInspector
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords;

    public IDataSourceNativeInspector Native => this;

    public async Task<DataSourceStorageState> Status(CancellationToken ct = default)
    {
        var (path, isMemory) = connections.DescribeSource(route.ConnectionString);
        if (!isMemory && !string.IsNullOrWhiteSpace(path) &&
            !path.Contains("://", StringComparison.Ordinal) &&
            !File.Exists(connections.AnchorDataSource(path)))
            return new DataSourceStorageState(DataSourceStorageStatus.Missing, "file-missing");

        try
        {
            await using var connection = await Open(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return new DataSourceStorageState(DataSourceStorageStatus.Ready, "ready");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (DuckDBException error)
        {
            return new DataSourceStorageState(
                DataSourceStorageStatus.Unavailable,
                $"duckdb-{error.Message.GetHashCode()}");
        }
        catch
        {
            return new DataSourceStorageState(DataSourceStorageStatus.Unavailable, "open-failed");
        }
    }

    public async Task<SourceContainerBatch> Containers(int take, string? providerContinuation, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var offset = Continuation(providerContinuation);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name, table_type
            FROM information_schema.tables
            WHERE table_schema = 'main' AND table_type IN ('BASE TABLE', 'VIEW')
            ORDER BY table_name
            LIMIT $take OFFSET $offset
            """;
        command.Parameters.Add(new DuckDBParameter("take", take + 1));
        command.Parameters.Add(new DuckDBParameter("offset", offset));
        var containers = new List<StorageContainerDescriptor>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            containers.Add(Descriptor(reader.GetString(0), Kind(reader.GetString(1)), null));
        var more = containers.Count > take;
        if (more) containers.RemoveAt(containers.Count - 1);
        return new SourceContainerBatch(
            containers,
            more ? StorageContainerPageCompletion.MoreAvailable : StorageContainerPageCompletion.Complete,
            more ? (offset + take).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    public async Task<StorageContainerReference> Resolve(StorageAddress address, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace.Count > 1 || address.Namespace.Count == 1 &&
            !string.Equals(address.Namespace[0], "main", StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException($"DuckDB container address '{address}' is outside the main database.");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_type FROM information_schema.tables WHERE table_schema = 'main' AND table_name = $name AND table_type IN ('BASE TABLE', 'VIEW')";
        command.Parameters.Add(new DuckDBParameter("name", address.Name));
        var kind = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new KeyNotFoundException($"DuckDB container '{address.Name}' does not exist on source '{route.Source}'.");
        return new RelationalContainerReference(route.Source, StorageAddress.From(address.Name), Kind(kind));
    }

    public async Task<StorageContainerDescriptor> Describe(StorageContainerReference reference, CancellationToken ct = default)
    {
        var value = Require(reference);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {DuckDbDialect.Quote(value.Address.Name)} LIMIT 0";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return Descriptor(value.Address.Name, value.ProviderKind, RelationalNeutralReader.Describe(reader));
    }

    public async Task<INeutralRecordReader> Sample(
        StorageContainerReference reference,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var value = Require(reference);
        var connection = await Open(ct).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {DuckDbDialect.Quote(value.Address.Name)} LIMIT $take";
        command.Parameters.Add(new DuckDBParameter("take", take + 1));
        return await RelationalNeutralReader.Open(connection, command, ct, take).ConfigureAwait(false);
    }

    private static string Kind(string providerKind) =>
        providerKind.Equals("VIEW", StringComparison.OrdinalIgnoreCase) ? "view" : "table";

    private StorageContainerDescriptor Descriptor(string name, string kind, IReadOnlyList<DataField>? shape)
    {
        var address = StorageAddress.From(name);
        var reference = new RelationalContainerReference(route.Source, address, kind);
        var view = string.Equals(kind, "view", StringComparison.OrdinalIgnoreCase);
        var writable = !view && route.Policy.Access == DataSourceAccess.ReadWrite;
        var operations = StorageContainerOperations.Describe | StorageContainerOperations.Sample |
                         StorageContainerOperations.Query |
                         (writable ? StorageContainerOperations.Write : StorageContainerOperations.None);
        var traits = StorageContainerTraits.Records |
                     (view
                         ? StorageContainerTraits.Virtual | StorageContainerTraits.ReadOnly
                         : StorageContainerTraits.Physical) |
                     (writable ? StorageContainerTraits.None : StorageContainerTraits.ReadOnly);
        return new StorageContainerDescriptor(reference, address, name, kind, traits, operations, shape);
    }

    private RelationalContainerReference Require(StorageContainerReference reference)
    {
        if (reference is not RelationalContainerReference relational ||
            !string.Equals(relational.Source, route.Source, StringComparison.OrdinalIgnoreCase))
            throw new StorageReferenceSourceMismatchException(route.Source, reference.Source);
        return relational;
    }

    private async Task<DuckDBConnection> Open(CancellationToken ct)
    {
        var connection = connections.Create(route.ConnectionString, route.Source, nonCreating: true);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static int Continuation(string? value) => string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, out var offset) && offset >= 0 ? offset :
        throw new ArgumentException("DuckDB container continuation is invalid.", nameof(value));
}
