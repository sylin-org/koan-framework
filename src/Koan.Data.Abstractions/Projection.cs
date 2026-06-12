namespace Koan.Data.Abstractions;

/// <summary>
/// A field-selection projection — the set of entity fields a query should return (sparse
/// fieldsets). Null on <see cref="QueryDefinition.Projection"/> means the full entity.
/// Adapters that can push column selection report <c>ProjectionHandled</c>; otherwise the
/// coordinator applies the projection in memory after materialization.
/// </summary>
public sealed record Projection(IReadOnlyList<string> Fields)
{
    public static Projection Of(params string[] fields) => new(fields);
}
