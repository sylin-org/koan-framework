using System.Data;
using System.Data.Common;
using Koan.Data.Relational.Ado;
using Microsoft.Data.SqlClient;

namespace Koan.Data.Connector.SqlServer.Runtime;

/// <summary>
/// A DateTime bound as <c>datetime2(7)</c>, exact to the tick. SqlClient infers the legacy <c>datetime</c> for a bare
/// DateTime, which rounds to 1/300 of a second before the value reaches the column or the comparison (DATA-0164).
/// </summary>
internal sealed record SqlServerDateTime(DateTime Value) : ISqlParameterValue
{
    /// <summary>The value as it binds: a DateTime to the tick, anything else unchanged.</summary>
    public static object? Exact(object? value) => value is DateTime dateTime ? new SqlServerDateTime(dateTime) : value;

    public void ApplyTo(DbParameter parameter)
    {
        var bound = (SqlParameter)parameter;
        bound.SqlDbType = SqlDbType.DateTime2;
        bound.Scale = 7;
        bound.Value = Value;
    }
}
