using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Models;
using Sora.AI.Contracts.Routing;

namespace Sora.AI.Web.Controllers;

[ApiController]
[Route(Constants.Routes.Base)]
public sealed class AiController : ControllerBase
{
    private readonly IAi _ai;
    private readonly IAiAdapterRegistry _registry;
    public AiController(IAi ai, IAiAdapterRegistry registry)
    { _ai = ai; _registry = registry; }

    [HttpGet(Constants.Routes.Health)]
    public IActionResult Health()
    {
        var total = _registry.All.Count;
        var state = total > 0 ? "Healthy" : "Unhealthy";
        return Ok(new { state, adapters = total });
    }

    [HttpGet(Constants.Routes.Adapters)]
    public IActionResult Adapters()
        => Ok(_registry.All.Select(a => new { a.Id, a.Name, a.Type }));

    [HttpGet(Constants.Routes.Models)]
    public async Task<IActionResult> Models(CancellationToken ct)
    {
        var results = new List<AiModelDescriptor>();
        foreach (var a in _registry.All)
        {
            try
            {
                var list = await a.ListModelsAsync(ct).ConfigureAwait(false);
                results.AddRange(list);
            }
            catch { /* ignore unavailable adapter */ }
        }
        return Ok(results);
    }

    [HttpGet(Constants.Routes.Capabilities)]
    public async Task<IActionResult> Capabilities(CancellationToken ct)
    {
        var caps = new List<AiCapabilities>();
        foreach (var a in _registry.All)
        {
            try { caps.Add(await a.GetCapabilitiesAsync(ct).ConfigureAwait(false)); }
            catch { /* ignore unavailable adapter */ }
        }
        return Ok(caps);
    }

    [HttpPost(Constants.Routes.Chat)]
    public async Task<ActionResult<AiChatResponse>> Chat([FromBody] AiChatRequest request, CancellationToken ct)
    {
        var res = await _ai.PromptAsync(request, ct).ConfigureAwait(false);
        return Ok(res);
    }

    [HttpPost(Constants.Routes.ChatStream)]
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

    [HttpPost(Constants.Routes.Embeddings)]
    public async Task<ActionResult<AiEmbeddingsResponse>> Embeddings([FromBody] AiEmbeddingsRequest request, CancellationToken ct)
    {
        var res = await _ai.EmbedAsync(request, ct).ConfigureAwait(false);
        return Ok(res);
    }
}
