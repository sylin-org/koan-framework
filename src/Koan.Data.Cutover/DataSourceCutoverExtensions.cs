using Koan.Data.Core;

namespace Koan.Data.Cutover;

public static class DataSourceCutoverExtensions
{
    /// <summary>Declares intent to prepare, verify, and atomically promote this configured source.</summary>
    public static DefaultRouteTransition PromoteToDefault(this DataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new DefaultRouteTransition(source.Name);
    }
}
