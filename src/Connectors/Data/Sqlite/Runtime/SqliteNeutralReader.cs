using Koan.Data.Abstractions;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteNeutralReader : INeutralRecordReader
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _command;
    private readonly SqliteDataReader _reader;
    private readonly NeutralRecordReaderCompletion _completedAs;
    private readonly int? _resultLimit;
    private NeutralRecordReaderCompletion _completion;
    private int _returned;
    private bool _complete;

    private SqliteNeutralReader(
        SqliteConnection connection,
        SqliteCommand command,
        SqliteDataReader reader,
        NeutralRecordReaderCompletion completedAs,
        int? resultLimit)
    {
        _connection = connection;
        _command = command;
        _reader = reader;
        _completedAs = completedAs;
        _resultLimit = resultLimit;
        _completion = completedAs;
        Fields = Describe(reader);
    }

    public IReadOnlyList<DataField> Fields { get; }
    public NeutralRecordReaderCompletion Completion => _complete ? _completion : NeutralRecordReaderCompletion.Complete;
    public bool HasAdditionalResultChannels => false;

    public static async Task<SqliteNeutralReader> Open(
        SqliteConnection connection,
        SqliteCommand command,
        NeutralRecordReaderCompletion completedAs,
        CancellationToken ct,
        int? resultLimit = null)
    {
        if (resultLimit is <= 0) throw new ArgumentOutOfRangeException(nameof(resultLimit));
        try
        {
            var reader = (SqliteDataReader)await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return new SqliteNeutralReader(connection, command, reader, completedAs, resultLimit);
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
        if (_resultLimit is not null && _returned == _resultLimit.Value)
        {
            _completion = await _reader.ReadAsync(ct).ConfigureAwait(false)
                ? NeutralRecordReaderCompletion.ProviderLimit
                : NeutralRecordReaderCompletion.Complete;
            _complete = true;
            return null;
        }
        if (!await _reader.ReadAsync(ct).ConfigureAwait(false))
        {
            _completion = _completedAs;
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
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    internal static IReadOnlyList<DataField> Describe(SqliteDataReader reader) =>
        Enumerable.Range(0, reader.FieldCount)
            .Select(index => new DataField(
                index,
                reader.GetName(index),
                SafeFieldType(reader, index),
                SafeTypeName(reader, index)))
            .ToArray();

    private static Type? SafeFieldType(SqliteDataReader reader, int ordinal)
    {
        try { return reader.GetFieldType(ordinal); }
        catch { return null; }
    }

    private static string? SafeTypeName(SqliteDataReader reader, int ordinal)
    {
        try { return reader.GetDataTypeName(ordinal); }
        catch { return null; }
    }
}
