using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// Shared test entity exercising the vector surface across adapters. Scalar fields cover
/// metadata-filter scenarios; the embedding itself is supplied separately via
/// <c>Vector&lt;TodoVector&gt;.Save</c>.
///
/// <para>
/// The explicit <see cref="StorageNameAttribute"/> keeps storage names short and provider-friendly:
/// fully-qualified type names contain dots that Weaviate rejects in GraphQL class names, and the
/// PascalCase form satisfies Weaviate's "class names must start with uppercase" rule.
/// </para>
/// </summary>
[StorageName("TodosVector")]
public sealed class TodoVector : Entity<TodoVector>
{
    public string Title { get; set; } = "";

    /// <summary>Category used as the metadata filter dimension in matrix specs.</summary>
    public string Category { get; set; } = "";

    /// <summary>Priority used as a numeric filter dimension.</summary>
    public int Priority { get; set; }
}
