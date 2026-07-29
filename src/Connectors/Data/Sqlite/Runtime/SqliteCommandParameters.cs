using Koan.Data.Abstractions;
using Koan.Data.Relational;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal static class SqliteCommandParameters
{
    public static void Bind(SqliteCommand command, IReadOnlyList<BoundOperationParameter> parameters)
    {
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(
                Name(parameter.Name),
                ComparableScalarEncoding.EncodeComparand(parameter.Value) ?? DBNull.Value);
    }

    private static string Name(string value) =>
        value.StartsWith('@') || value.StartsWith('$') || value.StartsWith(':') ? value : "@" + value;
}
