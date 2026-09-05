using Example.Approvals.Foundation.Web;
using ExpenseDesk.Domain;
using ExpenseDesk.Infrastructure;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseDesk.Web;

[Route(ExpenseConstants.Route)]
public sealed class ExpensesController : ApprovalController<ExpenseClaim>
{
    [HttpPost(ExpenseConstants.ReimburseRoute)]
    public async Task<ActionResult<ExpenseClaim>> Reimburse(string id, CancellationToken ct)
    {
        var claim = await ExpenseClaim.Get(id, ct);
        if (claim is null) return NotFound();
        claim.ReimbursedAt ??= DateTimeOffset.UtcNow;
        return Ok(await claim.Save(ct));
    }
}
