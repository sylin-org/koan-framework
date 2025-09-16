using System.Data;

namespace Koan.Data.Relational.Schema;

internal static class DbConnectionExtensions
{
    public static int Execute(this IDbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }
}