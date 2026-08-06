using System.Data.Common;
using Koan.Data.Abstractions;

namespace Koan.Data.Relational;

/// <summary>Streams one relational result set into Data's neutral record algebra.</summary>
public sealed class RelationalNeutralReader : INeutralRecordReader
{
    private readonly DbConnection _connection;
    private readonly DbCommand _command;
    private readonly DbDataReader _reader;
    private readonly DbTransaction? _transaction;
    private readonly int? _limit;
    private int _returned;
    private bool _complete;
    private NeutralRecordReaderCompletion _completion;

    private RelationalNeutralReader(
        DbConnection connection,
        DbCommand command,
        DbDataReader reader,
        int? limit,
        DbTransaction? transaction)
    {
        _connection = connection;
        _command = command;
        _reader = reader;
        _limit = limit;
        _transaction = transaction;
        Fields = Describe(reader);
    }

    public IReadOnlyList<DataField> Fields { get; }
    public NeutralRecordReaderCompletion Completion => _complete ? _completion : NeutralRecordReaderCompletion.Complete;
    public bool HasAdditionalResultChannels => false;

    public static async Task<RelationalNeutralReader> Open(
        DbConnection connection,
        DbCommand command,
        CancellationToken ct,
        int? limit = null,
        DbTransaction? transaction = null)
    {
        if (limit is <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        try
        {
            var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return new RelationalNeutralReader(connection, command, reader, limit, transaction);
        }
        catch
        {
            await command.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<DataRecord?> Read(CancellationToken ct = default)
    {
        if (_complete) return null;
        if (_limit is not null && _returned == _limit.Value)
        {
            _completion = await _reader.ReadAsync(ct).ConfigureAwait(false)
                ? NeutralRecordReaderCompletion.ProviderLimit
                : NeutralRecordReaderCompletion.Complete;
            _complete = true;
            return null;
        }
        if (!await _reader.ReadAsync(ct).ConfigureAwait(false))
        {
            _completion = NeutralRecordReaderCompletion.Complete;
            _complete = true;
            return null;
        }
        var values = new object?[Fields.Count];
        for (var index = 0; index < values.Length; index++)
            values[index] = _reader.IsDBNull(index) ? null : _reader.GetValue(index);
        _returned++;
        return new DataRecord(Fields, values);
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.DisposeAsync().ConfigureAwait(false);
        await _command.DisposeAsync().ConfigureAwait(false);
        if (_transaction is not null)
        {
            try { await _transaction.RollbackAsync().ConfigureAwait(false); }
            catch (InvalidOperationException) { }
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    public static IReadOnlyList<DataField> Describe(DbDataReader reader) =>
        Enumerable.Range(0, reader.FieldCount).Select(index => new DataField(
            index,
            reader.GetName(index),
            Safe(() => reader.GetFieldType(index)),
            Safe(() => reader.GetDataTypeName(index)))).ToArray();

    private static T? Safe<T>(Func<T> read)
    {
        try { return read(); }
        catch { return default; }
    }
}
