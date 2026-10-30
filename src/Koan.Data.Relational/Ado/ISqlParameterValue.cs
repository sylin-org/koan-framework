using System.Data.Common;

namespace Koan.Data.Relational.Ado;

/// <summary>
/// A parameter value that configures the provider parameter it is bound to: an array bound as one value, or a
/// table-valued parameter, where <see cref="SqlParameters"/> would otherwise bind a scalar or expand a sequence into an
/// IN list (DATA-0162). The value must be bindable again, so one translated filter can serve several commands.
/// </summary>
public interface ISqlParameterValue
{
    /// <summary>Sets the provider type and value of <paramref name="parameter"/>, whose name is already set.</summary>
    void ApplyTo(DbParameter parameter);
}
