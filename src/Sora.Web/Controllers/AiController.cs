using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Models;
using Sora.AI.Contracts.Routing;
using Sora.Web.Infrastructure;

namespace Sora.Web.Controllers;

[ApiController]
[Route(AiConstants.Routes.Base)] // controllers, not inline endpoints
public sealed class AiController : ControllerBase
{
    private readonly IAi _ai;
    private readonly IAiAdapterRegistry _registry;
    public AiController(IAi ai, IAiAdapterRegistry registry)
    { _ai = ai; _registry = registry; }

    [HttpGet(AiConstants.Routes.Health)]
    public IActionResult Health()
    {
        var total = _registry.All.Count;
        var state = total > 0 ? "Healthy" : "Unhealthy";
        return Ok(new { state, adapters = total });
    }

    [HttpGet(AiConstants.Routes.Adapters)]
    public IActionResult Adapters()
        => Ok(_registry.All.Select(a => new { a.Id, a.Name, a.Type }));

    [HttpPost(AiConstants.Routes.Chat)]
    public async Task<ActionResult<AiChatResponse>> Chat([FromBody] AiChatRequest request, CancellationToken ct)
    {
        var res = await _ai.PromptAsync(request, ct).ConfigureAwait(false);
        return Ok(res);
    }

    [HttpPost(AiConstants.Routes.ChatStream)]
    public async Task Stream([FromBody] AiChatRequest request, CancellationToken ct)
    {
        Response.StatusCode = 200;
    Response.ContentType = "text/event-stream";
    Response.Headers.CacheControl = "no-cache";
    Response.Headers.Pragma = "no-cache";
    Response.Headers.Connection = "keep-alive";
    Response.Headers["X-Accel-Buffering"] = "no"; // nginx
        await foreach (var chunk in _ai.StreamAsync(request, ct))
        {
            var payload = chunk.DeltaText ?? string.Empty;
            if (payload.Length == 0) continue;
            await Response.WriteAsync($"data: {payload}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    [HttpPost(AiConstants.Routes.Embeddings)]
    public async Task<ActionResult<AiEmbeddingsResponse>> Embeddings([FromBody] AiEmbeddingsRequest request, CancellationToken ct)
    {
        var res = await _ai.EmbedAsync(request, ct).ConfigureAwait(false);
        return Ok(res);
    }
}
