using ApprovalDesk.Domain;
using ApprovalDesk.Infrastructure;
using Example.Approvals.Foundation.Web;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;

namespace ApprovalDesk.Web;

[Route(PurchaseConstants.Route)]
public sealed class PurchasesController : ApprovalController<PurchaseRequest>
{
    [HttpPost(PurchaseConstants.OrderRoute)]
    public async Task<ActionResult<PurchaseRequest>> PlaceOrder(string id, CancellationToken ct)
    {
        var purchase = await PurchaseRequest.Get(id, ct);
        if (purchase is null) return NotFound();
        purchase.OrderNumber ??= PurchaseConstants.OrderPrefix + purchase.Id;
        return Ok(await purchase.Save(ct));
    }
}
