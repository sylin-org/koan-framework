using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.Web.Sse;

namespace Koan.AI.Web.Controllers;

[ApiController]
[Route(Constants.Routes.Base)]
public sealed class AiController : ControllerBase
{
    private readonly IAiPipeline _ai;
    private readonly IAiAdapterRegistry _registry;
    public AiController(IAiPipeline ai, IAiAdapterRegistry registry)
    { _ai = ai; _registry = registry; }

    [HttpGet(Constants.Routes.Health)]
    public IActionResult Health()
    {
        var total = _registry.All.Count;
        var state = total > 0 ? "Ready" : "Inactive";
        var message = total > 0
            ? $"{total} AI provider adapter(s) available."
            : "The AI HTTP projection is active, but no provider adapter is available.";
        return Ok(new { state, adapters = total, message });
    }

    [HttpGet(Constants.Routes.Adapters)]
    public IActionResult Adapters()
        => Ok(_registry.All.Select(a => new
        {
            a.Id,
            a.Name,
            a.Type,
            Capabilities = a.Capabilities.OrderBy(capability => capability)
        }));

    [HttpGet(Constants.Routes.Models)]
    public async Task<IActionResult> Models(CancellationToken ct)
    {
        var results = new List<AiModelDescriptor>();
        var failures = new List<object>();
        foreach (var a in _registry.All)
        {
            try
            {
                var list = await a.ListModels(ct);
                results.AddRange(list);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new
                {
                    adapterId = a.Id,
                    error = exception.Message
                });
            }
        }
        return Ok(new { models = results, failures });
    }

    [HttpGet(Constants.Routes.Capabilities)]
    public IActionResult Capabilities()
    {
        var caps = _registry.All.Select(a => new
        {
            a.Id,
            a.Type,
            Capabilities = a.Capabilities.OrderBy(capability => capability),
            ModelManagement = a.ModelManager is not null
        });
        return Ok(caps);
    }

    [HttpPost(Constants.Routes.Ocr)]
    public async Task<IActionResult> Ocr(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Image file is required." });

        using var ms = new System.IO.MemoryStream();
        await file.CopyToAsync(ms, ct);
        var imageBytes = ms.ToArray();

        var text = await Client.Ocr(imageBytes, new Contracts.Options.OcrOptions
        {
            MimeType = file.ContentType
        }, ct);

        return Ok(new { text });
    }

    [HttpPost(Constants.Routes.AdapterModelInstall)]
    public Task<IActionResult> InstallModel(string adapterId, [FromBody] AiModelOperationRequest request, CancellationToken ct)
        => ExecuteModelOperation(adapterId, request, static (manager, payload, token) => manager.EnsureInstalled(payload, token), ct);

    [HttpPost(Constants.Routes.AdapterModelRefresh)]
    public Task<IActionResult> RefreshModel(string adapterId, [FromBody] AiModelOperationRequest request, CancellationToken ct)
        => ExecuteModelOperation(adapterId, request, static (manager, payload, token) => manager.Refresh(payload, token), ct);

    [HttpPost(Constants.Routes.AdapterModelFlush)]
    public Task<IActionResult> FlushModel(string adapterId, [FromBody] AiModelOperationRequest request, CancellationToken ct)
        => ExecuteModelOperation(adapterId, request, static (manager, payload, token) => manager.Flush(payload, token), ct);

    [HttpPost(Constants.Routes.Chat)]
    public async Task<ActionResult<AiChatResponse>> Chat([FromBody] AiChatRequest request, CancellationToken ct)
    {
        var res = await _ai.Prompt(request, ct);
        return Ok(res);
    }

    [HttpPost(Constants.Routes.ChatStream)]
    public IActionResult ChatStream([FromBody] AiChatRequest request, CancellationToken ct)
        => Sse.Stream(StreamDeltas(request, ct));

    private async IAsyncEnumerable<string> StreamDeltas(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in _ai.Stream(request, ct))
        {
            var payload = chunk.DeltaText;
            if (string.IsNullOrEmpty(payload))
            {
                continue;
            }

            yield return payload;
        }
    }

    [HttpPost(Constants.Routes.Embeddings)]
    public async Task<ActionResult<AiEmbeddingsResponse>> Embeddings([FromBody] AiEmbeddingsRequest request, CancellationToken ct)
    {
        var res = await _ai.Embed(request, ct);
        return Ok(res);
    }

    private async Task<IActionResult> ExecuteModelOperation(
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

        var result = await operation(manager, normalized, ct);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, result);
        }

        return Ok(result);
    }
}
