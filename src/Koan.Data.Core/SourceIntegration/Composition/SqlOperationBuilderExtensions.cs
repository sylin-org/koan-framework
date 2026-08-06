using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>Compact native SQL/SQL++ binding leaf for registered operations.</summary>
public static class SqlOperationBuilderExtensions
{
    public static RecordQueryBuilder Sql(this RecordQueryBuilder builder, string commandText)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Native(new SqlOperationBinding(commandText));
    }

    public static ScalarQueryBuilder Sql(this ScalarQueryBuilder builder, string commandText)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Native(new SqlOperationBinding(commandText));
    }
}
