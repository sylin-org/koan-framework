using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>Compact provider-neutral native Function leaf for registered reads.</summary>
public static class FunctionOperationBuilderExtensions
{
    public static RecordQueryBuilder Function(this RecordQueryBuilder builder, string name, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Native(new FunctionOperationBinding(name, keys));
    }

    public static ScalarQueryBuilder Function(this ScalarQueryBuilder builder, string name, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Native(new FunctionOperationBinding(name, keys));
    }
}
