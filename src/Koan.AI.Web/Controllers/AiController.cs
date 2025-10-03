using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;

namespace Koan.AI.Web.Controllers;

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

    [HttpPost(Constants.Routes.AdapterModelInstall)]
    public Task<IActionResult> InstallModel(string adapterId, [FromBody] AiModelOperationRequest request, CancellationToken ct)
        => ExecuteModelOperationAsync(adapterId, request, static (manager, payload, token) => manager.EnsureInstalledAsync(payload, token), ct);

    [HttpPost(Constants.Routes.AdapterModelRefresh)]
    public Task<IActionResult> RefreshModel(string adapterId, [FromBody] AiModelOperationRequest request, CancellationToken ct)
        => ExecuteModelOperationAsync(adapterId, request, static (manager, payload, token) => manager.RefreshAsync(payload, token), ct);

    [HttpPost(Constants.Routes.AdapterModelFlush)]
    public Task<IActionResult> FlushModel(string adapterId, [FromBody] AiModelOperationRequest request, CancellationToken ct)
        => ExecuteModelOperationAsync(adapterId, request, static (manager, payload, token) => manager.FlushAsync(payload, token), ct);

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

    private async Task<IActionResult> ExecuteModelOperationAsync(
        string adapterId,
        AiModelOperationRequest request,
        Func<IAiModelManager, AiModelOperationRequest, CancellationToken, Task<AiModelOperationResult>> operation,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Model operation payload is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return BadRequest(new { message = "Model name is required." });
        }

        var adapter = _registry.Get(adapterId);
        if (adapter is null)
        {
            return NotFound(new { message = $"Adapter '{adapterId}' was not found." });
        }

        if (adapter.ModelManager is not IAiModelManager manager)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed, new { message = $"Adapter '{adapterId}' does not support model management." });
        }

        var normalized = request with
        {
            Model = request.Model.Trim(),
            Provenance = request.Provenance ?? new AiModelProvenance
            {
                RequestedBy = "ai-api",
                Reason = "manual",
                RequestedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["adapter_id"] = adapter.Id,
                    ["operation"] = operation.Method.Name
                }
            }
        };

        var result = await operation(manager, normalized, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, result);
        }

        return Ok(result);
    }
}
