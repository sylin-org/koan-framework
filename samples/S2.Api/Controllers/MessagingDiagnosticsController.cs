using Microsoft.AspNetCore.Mvc;
using Sora.Messaging;
using Sora.Messaging.Provisioning;

namespace S2.Api.Controllers;

[ApiController]
[Route("_diag/messaging")] // per Sora guardrails: routes in controllers
public class MessagingDiagnosticsController : ControllerBase
{
    private readonly IMessagingDiagnostics _diag;
    public MessagingDiagnosticsController(IMessagingDiagnostics diag) => _diag = diag;

    [HttpGet("{busCode}/plan")] // GET /_diag/messaging/{busCode}/plan
    public ActionResult<EffectiveMessagingPlan?> GetPlan(string busCode)
        => _diag.GetEffectivePlan(busCode) is { } plan ? Ok(plan) : NotFound();

    [HttpGet("{busCode}/provisioning")] // GET /_diag/messaging/{busCode}/provisioning
    public ActionResult<ProvisioningDiagnostics?> GetProvisioning(string busCode)
        => _diag.GetProvisioning(busCode) is { } prov ? Ok(prov) : NotFound();
}
