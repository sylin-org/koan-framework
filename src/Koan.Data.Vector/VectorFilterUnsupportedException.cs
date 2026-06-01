using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector;

/// <summary>
/// Thrown when a vector metadata filter cannot be fully pushed down to the provider — i.e. the
/// <c>VectorFilterCoordinator</c> split left a non-empty residual (AI-0036 §10 / DATA-0097 P1).
/// </summary>
/// <remarks>
/// This is the vector path's <b>residual-is-error</b> boundary. Unlike the entity path, there is no
/// in-memory floor to evaluate the residual against — post-kNN in-memory filtering silently
/// under-returns (DATA-0097 §3, a data-leak-shaped bug). So an operator/field/case-fold the adapter
/// cannot honour is a loud, precise error naming the offending operator, field, and provider, mapped
/// to HTTP 400 at the web layer (same family as <see cref="FilterParseException"/>). A capable-but-
/// unconfigured provider, a supported filter, and no filter all succeed silently — this fires only
/// at a genuine capability boundary.
/// </remarks>
public sealed class VectorFilterUnsupportedException : Exception
{
    /// <summary>The operator that could not be pushed (null when the residual is a composite node).</summary>
    public FilterOperator? Operator { get; }

    /// <summary>The metadata field path the unsupported clause targeted (null for composite residuals).</summary>
    public string? Field { get; }

    /// <summary>The provider that could not honour the clause.</summary>
    public string Provider { get; }

    public VectorFilterUnsupportedException(string provider, FilterOperator? op, string? field, string message)
        : base(message)
    {
        Provider = provider;
        Operator = op;
        Field = field;
    }

    /// <summary>Builds the exception for a residual filter, naming the first offending leaf where possible.</summary>
    public static VectorFilterUnsupportedException ForResidual(string provider, Filter residual)
    {
        var leaf = FirstLeaf(residual);
        if (leaf is not null)
        {
            return new VectorFilterUnsupportedException(
                provider, leaf.Operator, leaf.Field.ToString(),
                $"{provider} cannot push vector filter operator '{leaf.Operator}' on metadata field " +
                $"'{leaf.Field}'. Vector search has no in-memory residual evaluation, so this would " +
                $"silently under-return; declare the operator in the adapter's capabilities or remove it from the filter.");
        }

        return new VectorFilterUnsupportedException(
            provider, null, null,
            $"{provider} cannot fully push the requested vector filter (a composite clause is not " +
            $"wholly supported). Vector search has no in-memory residual evaluation, so this would silently under-return.");
    }

    private static FieldFilter? FirstLeaf(Filter f) => f switch
    {
        FieldFilter ff => ff,
        Not n => FirstLeaf(n.Operand),
        AllOf all => all.Operands.Select(FirstLeaf).FirstOrDefault(x => x is not null),
        AnyOf any => any.Operands.Select(FirstLeaf).FirstOrDefault(x => x is not null),
        _ => null
    };
}
