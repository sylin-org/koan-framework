using FirebirdSql.Data.FirebirdClient;
using Koan.Data.Abstractions;
using Koan.Data.Relational;

namespace Koan.Data.Connector.Firebird.Runtime;

internal sealed class FirebirdInspector(FirebirdRoute route) : IDataSourceInspectorAdapter
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords;

    public async Task<SourceContainerBatch> Containers(int take, string? providerContinuation, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var offset = Continuation(providerContinuation);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // User tables only: system-flag 0 and no view BLR. The offset rides the same RDB$ catalog order
        // every page reads, so an opaque numeric continuation resumes exactly where the last page stopped.
        command.CommandText = """
            SELECT TRIM(r.RDB$RELATION_NAME)
            FROM RDB$RELATIONS r
            WHERE r.RDB$SYSTEM_FLAG = 0 AND r.RDB$VIEW_BLR IS NULL
            ORDER BY 1
            ROWS @start + 1 TO @start + @take
            """;
        command.Parameters.Add("@start", offset);
        command.Parameters.Add("@take", take + 1);
        var values = new List<StorageContainerDescriptor>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Descriptor(reader.GetString(0), "BASE TABLE", null));
        var more = values.Count > take;
        if (more) values.RemoveAt(values.Count - 1);
        return new SourceContainerBatch(values,
            more ? StorageContainerPageCompletion.MoreAvailable : StorageContainerPageCompletion.Complete,
            more ? (offset + take).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    public async Task<StorageContainerReference> Resolve(StorageAddress address, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM RDB$RELATIONS r
            WHERE r.RDB$SYSTEM_FLAG = 0 AND r.RDB$VIEW_BLR IS NULL AND TRIM(r.RDB$RELATION_NAME) = @name
            """;
        command.Parameters.Add("@name", address.Name);
        var found = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
        return found
            ? new RelationalContainerReference(route.Source, StorageAddress.From(address.Name), "BASE TABLE")
            : throw new KeyNotFoundException($"Firebird container '{address}' does not exist on source '{route.Source}'.");
    }

    public async Task<StorageContainerDescriptor> Describe(StorageContainerReference reference, CancellationToken ct = default)
    {
        var value = Require(reference);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // One row is never read: the neutral reader lifts field metadata off the prepared statement, so
        // the description of an empty container is still its shape.
        command.CommandText = $"SELECT * FROM {FirebirdDialect.Quote(value.Address.Name)} ROWS 1";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var shape = RelationalNeutralReader.Describe(reader);
        return Descriptor(value.Address.Name, value.ProviderKind, shape);
    }

    public async Task<INeutralRecordReader> Sample(StorageContainerReference reference, int take, CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var value = Require(reference);
        var connection = await Open(ct).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {FirebirdDialect.Quote(value.Address.Name)} ROWS {take + 1}";
        return await RelationalNeutralReader.Open(connection, command, ct, take).ConfigureAwait(false);
    }

    private StorageContainerDescriptor Descriptor(string name, string kind, IReadOnlyList<DataField>? shape)
    {
        var address = StorageAddress.From(name);
        var reference = new RelationalContainerReference(route.Source, address, kind);
        return new StorageContainerDescriptor(reference, address, name, kind,
            StorageContainerTraits.Records | StorageContainerTraits.Physical,
            StorageContainerOperations.Describe | StorageContainerOperations.Sample | StorageContainerOperations.Query |
            StorageContainerOperations.Write, shape);
    }

    private RelationalContainerReference Require(StorageContainerReference reference)
    {
        if (reference is not RelationalContainerReference relational ||
            !string.Equals(relational.Source, route.Source, StringComparison.OrdinalIgnoreCase))
            throw new StorageReferenceSourceMismatchException(route.Source, reference.Source);
        return relational;
    }

    private async Task<FbConnection> Open(CancellationToken ct)
    {
        var connection = new FbConnection(route.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static int Continuation(string? value) => string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, out var offset) && offset >= 0 ? offset :
        throw new ArgumentException("Firebird container continuation is invalid.", nameof(value));
}
