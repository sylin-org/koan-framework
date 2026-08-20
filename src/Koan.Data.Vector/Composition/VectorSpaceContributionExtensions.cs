using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Vector;

/// <summary>
/// The seam another pillar uses to contribute a vector space it can derive from its own declarations.
///
/// <para>It exists because the pillar that knows the space is not the pillar that owns it. An Entity declares
/// its embedding model and width to <c>Koan.Data.AI</c>; that is everything a space needs, and requiring the
/// application to restate it in a composition callback is ceremony — a vector Entity should compose from a
/// bare <c>AddKoan()</c>. Vector cannot read <c>[Embedding]</c> itself (Data.AI references Vector, not the
/// reverse), so the contribution flows inward through this method.</para>
/// </summary>
public static class VectorSpaceContributionExtensions
{
    /// <summary>
    /// Contributes a derived space for <paramref name="entityType"/>. A space declared explicitly with
    /// <c>koan.Data.Source(...).Vector&lt;TEntity&gt;(...)</c> always outranks it, whichever composes first,
    /// and the first contribution for an Entity wins over later ones.
    /// </summary>
    public static IServiceCollection ContributeVectorSpace(
        this IServiceCollection services,
        Type entityType,
        VectorSpacePlan plan)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(plan);
        VectorSpaceDeclarationCatalog.DeclareDerived(services, entityType, plan);
        return services;
    }
}
