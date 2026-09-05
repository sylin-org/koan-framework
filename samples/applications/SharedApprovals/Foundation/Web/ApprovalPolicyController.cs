using Example.Approvals.Foundation.Infrastructure;
using Example.Approvals.Foundation.Policy;
using Microsoft.AspNetCore.Mvc;

namespace Example.Approvals.Foundation.Web;

[ApiController]
[Route(ApprovalConstants.PolicyRoute)]
public sealed class ApprovalPolicyController(ApprovalPolicyOptions options) : ControllerBase
{
    [HttpGet]
    public ApprovalPolicyOptions Get() => options;
}
