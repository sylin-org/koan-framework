using Example.Approvals.Foundation.Domain;
using Example.Approvals.Foundation.Infrastructure;
using Koan.Data.Core;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Example.Approvals.Foundation.Web;

public abstract class ApprovalController<TEntity> : EntityController<TEntity>
    where TEntity : ApprovalRequest<TEntity>, new()
{
    [HttpPost(ApprovalConstants.ApproveRoute)]
    public async Task<ActionResult<TEntity>> Approve(string id, CancellationToken ct)
    {
        var request = await ApprovalRequest<TEntity>.Get(id, ct);
        if (request is null) return NotFound();
        request.State = ApprovalState.Approved;
        return Ok(await request.Save(ct));
    }
}
