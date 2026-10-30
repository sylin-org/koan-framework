using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Relational.Ado;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;

namespace Koan.Data.Connector.SqlServer.Runtime;

/// <summary>
/// The table types a set binds through as one table-valued parameter, one per scalar a lone parameter binds as, so a
/// member in the set compares exactly as the member alone would and In answers what Eq answers member by member
/// (DATA-0162).
/// <list type="bullet">
/// <item>A type's name carries its shape: <c>koan_set_&lt;scalar&gt;_&lt;hash&gt;</c>, the hash of its one column's
/// definition. A changed shape is a new name, never a silent mismatch, so no type is ever migrated.</item>
/// <item>Koan creates the types idempotently in a source's schema when the source may create storage objects. A denied
/// <c>CREATE TYPE</c> is remembered as "may not create" and never fails readiness.</item>
/// <item>Each type found is checked against the definition Koan binds. A set needing a type that is missing or of another
/// shape is refused, naming it and the DDL that creates it.</item>
/// <item>Absence is never cached for good: a refusal marks the catalog stale, and the source's next operation reads it
/// again, at most once per <see cref="RecheckInterval"/>, so a type created after a refusal binds without a restart.</item>
/// </list>
/// One catalog per route and schema, shared by every repository on it.
/// </summary>
internal sealed class SqlServerSetTypes
{
    public const string Column = Infrastructure.Constants.SetTypes.Column;

    /// <summary>How often a catalog marked stale by a refusal is read again.</summary>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(1);

    private static readonly ConcurrentDictionary<string, SqlServerSetTypes> Catalogs = new(StringComparer.Ordinal);

    // Text members are converted through varbinary on the way out of the table: the result of a conversion from a
    // non-string type carries the coercible-default collation a parameter has, so it yields to the column's collation
    // exactly as a lone parameter does. Read straight from the table-valued parameter, the member would carry the
    // database's collation as an implicit label and conflict with a column declared under another collation.
    private static readonly SetKind NVarChar = new(
        "nvarchar", "nvarchar(4000)", new SqlMetaData(Column, SqlDbType.NVarChar, 4_000),
        $"CONVERT(nvarchar(4000), CONVERT(varbinary(8000), [{Column}]))");
    private static readonly SetKind NVarCharMax = new(
        "nvarchar_max", "nvarchar(max)", new SqlMetaData(Column, SqlDbType.NVarChar, SqlMetaData.Max),
        $"CONVERT(nvarchar(max), CONVERT(varbinary(max), [{Column}]))");

    // A lone decimal parameter takes the precision and scale of its own value, and no one decimal(p, s) column holds
    // every value exactly (1/3 needs scale 28, 10^14 needs fifteen integral digits). A sql_variant column keeps each
    // member as the decimal its value would bind as alone, and compares within the exact-numeric family by value.
    private static readonly SetKind Decimal = new("decimal", "sql_variant", new SqlMetaData(Column, SqlDbType.Variant), null);

    // Each kind is the SqlDbType SqlClient infers for a lone parameter of that CLR type. A TimeSpan, DateOnly or TimeOnly
    // on a document field reaches a set already encoded (ticks as bigint, the others as text) and binds through those
    // kinds; raw, from a typed column, it has no kind here — no typed column of them round-trips on SQL Server — so such
    // a list stays one parameter per member.
    private static readonly IReadOnlyDictionary<Type, SetKind> ByType = new Dictionary<Type, SetKind>
    {
        [typeof(Guid)] = new("uniqueidentifier", "uniqueidentifier", new SqlMetaData(Column, SqlDbType.UniqueIdentifier), null),
        [typeof(DateTime)] = new("datetime", "datetime", new SqlMetaData(Column, SqlDbType.DateTime), null),
        [typeof(DateTimeOffset)] = new("datetimeoffset", "datetimeoffset(7)", new SqlMetaData(Column, SqlDbType.DateTimeOffset, 0, 7), null),
        [typeof(double)] = new("float", "float", new SqlMetaData(Column, SqlDbType.Float), null),
        [typeof(float)] = new("real", "real", new SqlMetaData(Column, SqlDbType.Real), null),
        [typeof(bool)] = new("bit", "bit", new SqlMetaData(Column, SqlDbType.Bit), null),
        [typeof(long)] = new("bigint", "bigint", new SqlMetaData(Column, SqlDbType.BigInt), null),
        [typeof(int)] = new("int", "int", new SqlMetaData(Column, SqlDbType.Int), null),
        [typeof(short)] = new("smallint", "smallint", new SqlMetaData(Column, SqlDbType.SmallInt), null),
        [typeof(byte)] = new("tinyint", "tinyint", new SqlMetaData(Column, SqlDbType.TinyInt), null),
    };

    private static readonly IReadOnlyList<SetKind> Kinds = ByType.Values
        .Append(NVarChar).Append(NVarCharMax).Append(Decimal)
        .DistinctBy(static kind => kind.Name)
        .ToArray();

    private readonly SemaphoreSlim _gate = new(1, 1);
    // Type name → its column as found ("[koan_member] float NOT NULL"); null until first read.
    private volatile IReadOnlyDictionary<string, string>? _found;
    private volatile bool _created;
    private volatile bool _mayCreate;
    private volatile bool _creationDenied;
    private volatile bool _stale;
    private long _readAt;

    private SqlServerSetTypes(string schema) => Schema = schema;

    public string Schema { get; }

    public static SqlServerSetTypes For(string route, string schema) =>
        Catalogs.GetOrAdd($"{route}|{schema}", static (_, name) => new SqlServerSetTypes(name), schema);

    /// <summary>
    /// Creates every missing set type in <see cref="Schema"/> when the source may create storage objects and creation was
    /// not denied, then reads which exist and their definitions. Runs once per catalog, and again after a refusal or a
    /// server's "cannot find data type" marked it stale and <see cref="RecheckInterval"/> passed — creating again whatever
    /// is missing then, so a type dropped later is recreated. A failure here never fails the caller's operation: what
    /// could not be read is treated as missing, and a set needing it is refused by name.
    /// </summary>
    public async Task Ensure(Func<CancellationToken, Task<SqlConnection>> open, bool mayCreate, CancellationToken ct)
    {
        if (Current(mayCreate)) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Current(mayCreate)) return;
            _mayCreate = mayCreate;
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var unreadable = false;
            try
            {
                await using var connection = await open(ct).ConfigureAwait(false);
                if (mayCreate && !_creationDenied)
                {
                    foreach (var kind in Kinds)
                    {
                        await using var create = connection.CreateCommand();
                        create.CommandText = Ddl(kind);
                        try
                        {
                            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }
                        // Another process created it between the check and the create.
                        catch (SqlException error) when (error.Number is 219 or 2714) { }
                        // Denied (262 and its kin), or any other refusal: this source may not create types here.
                        catch (SqlException) when (!ct.IsCancellationRequested)
                        {
                            _creationDenied = true;
                            break;
                        }
                    }
                    if (!_creationDenied) _created = true;
                }
                await using var read = connection.CreateCommand();
                read.CommandText = """
                    SELECT tt.name, c.name, TYPE_NAME(c.system_type_id), c.max_length, c.precision, c.scale, c.is_nullable
                      FROM sys.table_types AS tt
                      JOIN sys.columns AS c ON c.object_id = tt.type_table_object_id
                     WHERE tt.schema_id = SCHEMA_ID(@schema) AND tt.name LIKE @prefix
                     ORDER BY tt.name, c.column_id
                    """;
                read.Parameters.AddWithValue("@schema", Schema);
                read.Parameters.AddWithValue("@prefix", Infrastructure.Constants.SetTypes.Prefix.Replace("_", "[_]") + "%");
                await using var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var column = Render(
                        reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5),
                        reader.GetBoolean(6));
                    var name = reader.GetString(0);
                    found[name] = found.TryGetValue(name, out var earlier) ? earlier + ", " + column : column;
                }
            }
            catch (DbException) when (!ct.IsCancellationRequested)
            {
                // Unreadable: nothing is known to exist, and a large set is refused by name until a later read succeeds.
                unreadable = true;
            }
            _found = found;
            Interlocked.Exchange(ref _readAt, Stopwatch.GetTimestamp());
            _stale = unreadable;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Marks the catalog stale at once: the server said a type it holds is gone, so the next ensure reads it again (and
    /// creates what is missing) without waiting for <see cref="RecheckInterval"/>.
    /// </summary>
    public void Invalidate()
    {
        _stale = true;
        Interlocked.Exchange(ref _readAt, 0);
    }

    private bool Current(bool mayCreate)
    {
        if (_found is null) return false;
        if (mayCreate && !_created && !_creationDenied) return false;
        return !_stale || Stopwatch.GetElapsedTime(Interlocked.Read(ref _readAt)) < RecheckInterval;
    }

    /// <summary>The set type a set of <paramref name="members"/> binds through, when they share one; else null.</summary>
    public static SetKind? KindOf(IReadOnlyList<object?> members)
    {
        if (members.Count == 0 || members[0] is not { } first) return null;
        if (first is string)
        {
            var longest = 0;
            foreach (var member in members)
            {
                if (member is not string text) return null;
                longest = Math.Max(longest, text.Length);
            }
            return longest <= 4_000 ? NVarChar : NVarCharMax;
        }
        // A lone char binds as nvarchar, so a set of chars binds as text.
        if (first is char)
        {
            foreach (var member in members)
                if (member is not char) return null;
            return NVarChar;
        }
        if (first is decimal)
        {
            foreach (var member in members)
                if (member is not decimal) return null;
            return Decimal;
        }
        if (!ByType.TryGetValue(first.GetType(), out var kind)) return null;
        var type = first.GetType();
        foreach (var member in members)
            if (member is null || member.GetType() != type) return null;
        return kind;
    }

    /// <summary>
    /// The reason <paramref name="kind"/> cannot bind here, or null when it can (or nothing is known yet). A refusal marks
    /// the catalog stale, so the source's next operation reads it again.
    /// </summary>
    public string? Refusal(SetKind kind)
    {
        if (_found is not { } found) return null;
        var name = TypeName(kind);
        if (found.TryGetValue(name, out var column))
        {
            if (string.Equals(column, Expected(kind), StringComparison.OrdinalIgnoreCase)) return null;
            _stale = true;
            var drop = $"DROP TYPE {QualifiedName(kind)}";
            return $"the table type {QualifiedName(kind)} exists here as ({column}), not as the ({Expected(kind)}) Koan " +
                   "binds a set through, and Koan never binds through a type of another shape. " +
                   (CreatesHere
                       ? $"Drop it ({drop}) and Koan creates it at this source's next operation"
                       : $"Replace it: {drop}; CREATE TYPE {QualifiedName(kind)} AS TABLE ({Expected(kind)})");
        }
        _stale = true;
        return $"the table type {QualifiedName(kind)} does not exist here" +
               (_creationDenied
                   ? $", and this source's login was denied creating it. Create it once: {Ddl(kind)}"
                   : !_mayCreate
                       ? $", and this source may not create storage objects (its lifecycle, access or DDL policy). Create it once: {Ddl(kind)}"
                       : $"; Koan creates it at this source's next operation, or create it now: {Ddl(kind)}");
    }

    // Whether the last ensure could create types here.
    private bool CreatesHere => _mayCreate && !_creationDenied;

    public string QualifiedName(SetKind kind) => $"[{Schema.Replace("]", "]]")}].[{TypeName(kind)}]";

    public string Ddl(SetKind kind) =>
        $"IF TYPE_ID(N'{QualifiedName(kind).Replace("'", "''")}') IS NULL " +
        $"CREATE TYPE {QualifiedName(kind)} AS TABLE ({Expected(kind)})";

    private static string Expected(SetKind kind) => $"[{Column}] {kind.SqlType} NOT NULL";

    /// <summary>koan_set_&lt;scalar&gt;_&lt;first eight hex digits of the SHA-256 of its column definition&gt;.</summary>
    private static string TypeName(SetKind kind) =>
        Infrastructure.Constants.SetTypes.Prefix + kind.Name + "_" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Expected(kind))))[..8];

    private static string Render(string column, string type, short maxLength, byte precision, byte scale, bool nullable)
    {
        var shape = type switch
        {
            "nvarchar" or "varchar" or "nchar" or "char" or "varbinary" or "binary" => maxLength == -1
                ? $"{type}(max)"
                : $"{type}({(type.StartsWith('n') ? maxLength / 2 : maxLength)})",
            "time" or "datetime2" or "datetimeoffset" => $"{type}({scale})",
            "decimal" or "numeric" => $"{type}({precision}, {scale})",
            _ => type
        };
        return $"[{column}] {shape} {(nullable ? "NULL" : "NOT NULL")}";
    }

    /// <summary>
    /// A scalar a set binds as: the type name's scalar part, its column type, the record shape, and the expression the
    /// comparison reads a member through (null: the column itself).
    /// </summary>
    internal sealed record SetKind(string Name, string SqlType, SqlMetaData Metadata, string? MemberSql)
    {
        public string Member => MemberSql ?? $"[{Column}]";
    }
}

/// <summary>A set bound as one table-valued parameter of a <see cref="SqlServerSetTypes"/> type (DATA-0162).</summary>
internal sealed class SqlServerSetParameter(string typeName, SqlMetaData metadata, IReadOnlyList<object?> members)
    : ISqlParameterValue
{
    public void ApplyTo(DbParameter parameter)
    {
        var table = (SqlParameter)parameter;
        table.SqlDbType = SqlDbType.Structured;
        table.TypeName = typeName;
        table.Value = Records();
    }

    // A fresh enumeration per command, so one translated filter can serve a query and its count.
    private IEnumerable<SqlDataRecord> Records()
    {
        foreach (var member in members)
        {
            var record = new SqlDataRecord(metadata);
            record.SetValue(0, member is char character ? character.ToString() : member);
            yield return record;
        }
    }
}
