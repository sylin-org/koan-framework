namespace Koan.Data.Relational.Ado;

/// <summary>
/// An array bound as one parameter — a PostgreSQL array a dialect compares with <c>= ANY(...)</c> — where
/// <see cref="SqlParameters"/> would otherwise expand a sequence into an IN list of one parameter per element. A set
/// bound this way meets no per-statement parameter limit (DATA-0162).
/// </summary>
/// <param name="Values">A typed array the provider binds as its native array type.</param>
public sealed record SqlArrayParameter(Array Values) : ISqlParameterValue
{
    public void ApplyTo(System.Data.Common.DbParameter parameter) => parameter.Value = Values;
}
