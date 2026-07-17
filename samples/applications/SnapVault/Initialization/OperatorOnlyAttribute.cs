using Koan.Data.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SnapVault.Initialization;

/// <summary>
/// SnapVault step 5e — the STUDIO-OPERATOR floor. Once the guest subject is live (an invited client with a
/// constrained <see cref="Subject"/>), the studio's management surface must refuse them: writes are not gated by the
/// SEC-0008 access axis (it is a read axis), and Event/Collection are only tenant-scoped (not <c>[AccessScoped]</c>),
/// so the ambient tenant alone would let a guest list every event or DELETE the studio's photos. This filter admits
/// only an <b>unconstrained</b> subject (a studio operator; the tenant axis still isolates them) and refuses a guest
/// (constrained) or a subject-less request with 403. Applied to the operator controllers wholesale, and to the
/// studio write/aggregate actions on the mixed <c>PhotosController</c>; the guest's own surface (proofing writes,
/// access-scoped gallery reads) is deliberately NOT gated by it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OperatorOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var subject = Subject.Current;
        if (subject is null || subject.IsConstrained)
            context.Result = new ObjectResult(new { error = "Studio operator access required." })
            { StatusCode = StatusCodes.Status403Forbidden };
    }
}
