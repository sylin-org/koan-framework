using Koan.Web.Hooks;

namespace Koan.Web.Controllers;

/// <summary>How a refused form reaches the page its controller hands (WEB-0089): <see cref="Filters.RefusedWrite"/> asks it of an
/// <see cref="EntityController{TEntity,TKey}"/>, whose <c>RefusedPage</c> answers.</summary>
internal interface IRefusedPage
{
    ValueTask<object?> RefusedPage(EntityOperationKind operation, Problem refused, CancellationToken ct);
}
