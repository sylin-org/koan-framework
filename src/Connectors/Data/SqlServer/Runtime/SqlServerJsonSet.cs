using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Koan.Data.Relational.Ado;
using Microsoft.Data.SqlClient;

namespace Koan.Data.Connector.SqlServer.Runtime;

/// <summary>
/// A set bound as one JSON text and read through <c>OPENJSON</c>, so a set of any size binds one parameter and the store
/// holds nothing for it (DATA-0162). Each member is read as the type a lone parameter of it binds as, so In answers what Eq
/// answers. Members of mixed types, or of a type read here no other way, stay one parameter per member.
/// </summary>
internal static class SqlServerJsonSet
{
    private const string Column = "koan_member";

    // Text is converted through varbinary on the way out: the result of a conversion from a non-string type carries the
    // coercible-default collation a parameter has, so it yields to the column's collation as a lone parameter does.
    private const string NVarChar = $"CONVERT(nvarchar(4000), CONVERT(varbinary(8000), {Column}))";
    private const string NVarCharMax = $"CONVERT(nvarchar(max), CONVERT(varbinary(max), {Column}))";

    // The server's parser lands some decimal text a unit off, so a real number travels as the integers that are its exact
    // value, mantissa times two to the exponent. Both factors and their product are exact in a float.
    private const string RealColumns = "koan_mantissa bigint '$[0]', koan_exponent int '$[1]'";
    private const string Float = "CONVERT(float, koan_mantissa) * POWER(CONVERT(float, 2), koan_exponent)";

    private static readonly IReadOnlyDictionary<Type, string> SqlTypes = new Dictionary<Type, string>
    {
        [typeof(Guid)] = "uniqueidentifier",
        [typeof(SqlServerDateTime)] = "datetime2(7)",
        [typeof(DateTimeOffset)] = "datetimeoffset(7)",
        [typeof(bool)] = "bit",
        [typeof(long)] = "bigint",
        [typeof(int)] = "int",
        [typeof(short)] = "smallint",
        [typeof(byte)] = "tinyint",
    };

    /// <summary>A <c>SELECT</c> of the members as one column, or null where these members stay one parameter each.</summary>
    public static string? Select(IReadOnlyList<object?> members, Func<object?, string> bind)
    {
        if (members.Count == 0 || members[0] is not { } first) return null;
        var type = first.GetType();
        for (var index = 1; index < members.Count; index++)
            if (members[index]?.GetType() != type) return null;

        if (first is string)
            return members.Any(static member => ((string)member!).Length > 4_000)
                ? From("nvarchar(max)", NVarCharMax, members, bind)
                : From("nvarchar(4000)", NVarChar, members, bind);
        if (first is char) return From("nvarchar(4000)", NVarChar, members, bind);
        if (first is double) return FromColumns(RealColumns, Float, members, bind);
        if (first is float) return FromColumns(RealColumns, $"CONVERT(real, {Float})", members, bind);

        // A lone decimal binds at the precision and scale of its own value, and no one decimal(p, s) holds every value
        // exactly. Each shape reads as its own decimal; as sql_variants they compare within the exact-numeric family by value.
        if (first is decimal)
            return string.Join(" UNION ALL ", members
                .GroupBy(static member => Shape((decimal)member!))
                .Select(shape => From($"decimal({shape.Key.Precision}, {shape.Key.Scale})",
                    $"CONVERT(sql_variant, {Column})", shape.ToList(), bind)));

        return SqlTypes.TryGetValue(type, out var sqlType) ? From(sqlType, Column, members, bind) : null;
    }

    private static string From(string sqlType, string member, IReadOnlyList<object?> members, Func<object?, string> bind) =>
        FromColumns($"{Column} {sqlType} '$'", member, members, bind);

    private static string FromColumns(string columns, string member, IReadOnlyList<object?> members, Func<object?, string> bind) =>
        $"SELECT {member} FROM OPENJSON({bind(new Parameter(Encode(members)))}) WITH ({columns})";

    private static (byte Precision, byte Scale) Shape(decimal value)
    {
        var shape = new SqlDecimal(value);
        return (shape.Precision, shape.Scale);
    }

    private static string Encode(IReadOnlyList<object?> members)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var member in members)
                switch (member)
                {
                    case string text: json.WriteStringValue(text); break;
                    case char character: json.WriteStringValue(character.ToString()); break;
                    case Guid id: json.WriteStringValue(id); break;
                    case SqlServerDateTime at:
                        json.WriteStringValue(at.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture));
                        break;
                    case DateTimeOffset at:
                        json.WriteStringValue(at.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture));
                        break;
                    case double number: WriteReal(json, number); break;
                    case float number: WriteReal(json, number); break;
                    case decimal number: json.WriteNumberValue(number); break;
                    case bool flag: json.WriteNumberValue(flag ? 1 : 0); break;
                    case long number: json.WriteNumberValue(number); break;
                    case int number: json.WriteNumberValue(number); break;
                    case short number: json.WriteNumberValue(number); break;
                    case byte number: json.WriteNumberValue(number); break;
                    default: throw new NotSupportedException($"A set of {member?.GetType().Name} has no JSON form here.");
                }
            json.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static void WriteReal(Utf8JsonWriter json, double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var biased = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        var mantissa = biased == 0 ? fraction : fraction | (1L << 52);
        json.WriteStartArray();
        json.WriteNumberValue(bits < 0 ? -mantissa : mantissa);
        json.WriteNumberValue(biased == 0 ? -1074 : biased - 1075);
        json.WriteEndArray();
    }

    // One declared type at every size, so a statement's plan does not depend on how long its list is.
    private sealed class Parameter(string json) : ISqlParameterValue
    {
        public void ApplyTo(DbParameter parameter)
        {
            var text = (SqlParameter)parameter;
            text.SqlDbType = SqlDbType.NVarChar;
            text.Size = -1;
            text.Value = json;
        }
    }
}
