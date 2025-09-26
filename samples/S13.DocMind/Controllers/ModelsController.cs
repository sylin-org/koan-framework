using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Adapters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using S13.DocMind.Contracts;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ModelsController : ControllerBase
{
    private readonly IAiAdapterRegistry _registry;
    private readonly ILogger<ModelsController> _logger;

    public ModelsController(IAiAdapterRegistry registry, ILogger<ModelsController> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ModelDescriptor>>> GetAsync(CancellationToken cancellationToken)
    {
        var models = new List<ModelDescriptor>();
        foreach (var adapter in _registry.All)
        {
            IReadOnlyList<Koan.AI.Contracts.Models.AiModelDescriptor> adapterModels;
            try
            {
                adapterModels = await adapter.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list models for adapter {Adapter}", adapter.Id);
                adapterModels = Array.Empty<Koan.AI.Contracts.Models.AiModelDescriptor>();
            }

            var capabilities = new Dictionary<string, string>
            {
                ["supportsChat"] = "unknown",
                ["supportsStreaming"] = "unknown",
                ["supportsEmbeddings"] = "unknown"
            };
            try
            {
                var caps = await adapter.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
                capabilities["supportsChat"] = caps.SupportsChat.ToString();
                capabilities["supportsStreaming"] = caps.SupportsStreaming.ToString();
                capabilities["supportsEmbeddings"] = caps.SupportsEmbeddings.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Capabilities unavailable for adapter {Adapter}", adapter.Id);
            }

            models.Add(new ModelDescriptor
            {
                AdapterId = adapter.Id,
                AdapterName = adapter.Name,
                Type = adapter.Type,
                Models = adapterModels.Select(m => m.Id ?? m.Name ?? string.Empty).Where(m => !string.IsNullOrWhiteSpace(m)).ToList(),
                Capabilities = capabilities
            });
        }

        return Ok(models);
    }

    [HttpGet("providers")]
    public ActionResult<IEnumerable<object>> Providers()
        => Ok(_registry.All.Select(a => new { a.Id, a.Name, a.Type }));

    [HttpPost("install")]
    public IActionResult Install([FromBody] ModelInstallRequest request)
    {
        var adapter = _registry.Get(request.AdapterId);
        if (adapter is null) return NotFound();
        _logger.LogInformation("Model install request queued for {Model} via {Adapter}", request.Model, request.AdapterId);
        return Accepted(new { request.AdapterId, request.Model, Status = "queued" });
    }
}
