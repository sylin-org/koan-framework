using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteInspector(SqliteRoute route, SqliteConnections connections) :
    IDataSourceInspectorAdapter,
    IDataSourceStatusInspector
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords;

    public IDataSourceNativeInspector Native => this;

    public async Task<DataSourceStorageState> Status(CancellationToken ct = default)
    {
        SqliteConnectionStringBuilder builder;
        try
        {
            builder = connections.Parse(route.ConnectionString);
        }
        catch
        {
            return new DataSourceStorageState(DataSourceStorageStatus.Unavailable, "connection-invalid");
        }

        var memory = builder.Mode == SqliteOpenMode.Memory ||
                     string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
        var uri = builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!memory && !uri && !File.Exists(connections.AnchorDataSource(builder.DataSource)))
            return new DataSourceStorageState(DataSourceStorageStatus.Missing, "file-missing");

        try
        {
            await using var connection = await Open(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
                ? new DataSourceStorageState(DataSourceStorageStatus.Ready, "ready")
                : new DataSourceStorageState(DataSourceStorageStatus.Unavailable, "integrity-check-failed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException error)
        {
            return new DataSourceStorageState(
                DataSourceStorageStatus.Unavailable,
                $"sqlite-{error.SqliteErrorCode}");
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
            SELECT name, type
            FROM sqlite_schema
            WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'
            ORDER BY name
            LIMIT @take OFFSET @offset
            """;
        command.Parameters.AddWithValue("@take", take + 1);
        command.Parameters.AddWithValue("@offset", offset);
        var containers = new List<StorageContainerDescriptor>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            containers.Add(Descriptor(reader.GetString(0), reader.GetString(1), null));
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
            throw new KeyNotFoundException($"SQLite container address '{address}' is outside the main database.");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type FROM sqlite_schema WHERE name=@name AND type IN ('table', 'view')";
        command.Parameters.AddWithValue("@name", address.Name);
        var kind = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new KeyNotFoundException($"SQLite container '{address.Name}' does not exist on source '{route.Source}'.");
        return new RelationalContainerReference(route.Source, StorageAddress.From(address.Name), kind);
    }

    public async Task<StorageContainerDescriptor> Describe(StorageContainerReference reference, CancellationToken ct = default)
    {
        var value = Require(reference);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {SqliteDialect.Quote(value.Address.Name)} LIMIT 0";
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
        command.CommandText = $"SELECT * FROM {SqliteDialect.Quote(value.Address.Name)} LIMIT @take";
        command.Parameters.AddWithValue("@take", take + 1);
        return await RelationalNeutralReader.Open(connection, command, ct, take).ConfigureAwait(false);
    }

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

    private async Task<SqliteConnection> Open(CancellationToken ct)
    {
        var connection = connections.Create(route.ConnectionString, route.Source, nonCreating: true);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static int Continuation(string? value) => string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, out var offset) && offset >= 0 ? offset :
        throw new ArgumentException("SQLite container continuation is invalid.", nameof(value));
}
