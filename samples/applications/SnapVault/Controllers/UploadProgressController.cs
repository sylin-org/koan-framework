using Koan.Web.Sse.Mvc;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Progress;

namespace SnapVault.Controllers;

/// <summary>
/// Live upload-progress stream (SnapVault D4) — the SSE endpoint that replaced the SignalR <c>PhotoProcessingHub</c>.
/// The browser opens one <c>EventSource("/api/photos/progress/{batchId}")</c> after an upload POST returns the batch
/// id; the server streams a <c>PhotoProgress</c> frame per photo state-change and a terminal <c>JobCompleted</c>
/// frame, then closes. There is no subscribe/unsubscribe, no group management, no push fan-out: the stream is a
/// read-projection of the durable jobs ledger (<see cref="UploadProgressProjection"/>).
///
/// <para><b>Isolation is inherited, not hardcoded (deliberate — Reference = Intent).</b> There is no
/// <c>[Authorize]</c> and no ad-hoc tenant check: the projection reads <c>PhotoProcessingJob</c> through the data
/// layer, so the endpoint is exactly as isolated as the app opted into — identical to how step 3's media serving
/// inherits the access axis via <c>Data&lt;T&gt;.Get</c> rather than a controller gate. In a tenancy app under the
/// prod-closed posture, an unauthenticated request has no tenant ambient ⇒ the read <b>fails closed</b> structurally
/// (SEC-0008 / the flagship's proven behavior); an authenticated operator gets a studio-scoped read (the
/// <c>Koan.Identity.Tenancy</c> <c>AfterAuthentication</c> middleware sets the ambient). In the dev-open posture it
/// is permissive by design — like every other read in the app. In an app that references neither tenancy nor auth,
/// there is nothing to isolate and it simply works. Hardcoding <c>[Authorize]</c> would break that open-app case,
/// be redundant under prod-closed, and contradict the structural-isolation moat this conversion is built on.</para>
///
/// <para>The upload POST that mints the batch id and submits the <c>PhotoProcessingJob</c>s is rebuilt in step 5
/// (its ingest pipeline is the <c>ctx.Progress</c> emitter this endpoint reads); step 4 owns only the read side.</para>
/// </summary>
[ApiController]
[Route("api/photos")]
public sealed class UploadProgressController : ControllerBase
{
    [HttpGet("progress/{batchId}")]
    public IActionResult Progress(string batchId)
        => SseActionResult.StreamEnvelopes(UploadProgressProjection.StreamAsync(batchId, HttpContext.RequestAborted));
}
