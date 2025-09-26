using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using S13.DocMind.Services;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    private readonly IModelCatalogService _catalogService;
    private readonly IModelInstallationQueue _installationQueue;

    public ModelsController(IModelCatalogService catalogService, IModelInstallationQueue installationQueue)
    {
        _catalogService = catalogService;
        _installationQueue = installationQueue;
    }

    [HttpGet("available")]
    public async Task<ActionResult> GetAvailable(CancellationToken cancellationToken)
    {
        var models = await _catalogService.GetAvailableModelsAsync(cancellationToken);
        return Ok(models);
    }

    [HttpGet("installed")]
    public async Task<ActionResult> GetInstalled(CancellationToken cancellationToken)
    {
        var models = await _catalogService.GetInstalledModelsAsync(cancellationToken);
        return Ok(models);
    }

    [HttpGet("search")]
    public async Task<ActionResult> Search([FromQuery] string? query, [FromQuery] string? provider, CancellationToken cancellationToken)
    {
        var models = await _catalogService.SearchModelsAsync(query, provider, cancellationToken);
        return Ok(models);
    }

    [HttpPost("{modelName}/install")]
    public async Task<ActionResult> Install(string modelName, [FromBody] InstallModelRequest? request, CancellationToken cancellationToken)
    {
        var provider = string.IsNullOrEmpty(request?.Provider) ? "ollama" : request!.Provider!;
        var available = await _catalogService.GetAvailableModelsAsync(cancellationToken);
        var targetModel = available.FirstOrDefault(model =>
            string.Equals(model.Name, modelName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(model.Provider, provider, StringComparison.OrdinalIgnoreCase));

        if (targetModel is null)
        {
            return NotFound(new { message = $"Model {modelName} for provider {provider} was not found" });
        }

        var status = await _installationQueue.EnqueueAsync(provider, modelName, cancellationToken);
        var routeValues = new { installationId = status.InstallationId };
        return AcceptedAtAction(nameof(GetInstallationStatus), routeValues, ModelInstallationStatusDto.From(status));
    }

    [HttpGet("installations")]
    public ActionResult GetInstallations()
    {
        var statuses = _installationQueue.GetStatuses().Select(ModelInstallationStatusDto.From).ToList();
        return Ok(statuses);
    }

    [HttpGet("installations/{installationId:guid}")]
    public ActionResult GetInstallationStatus(Guid installationId)
    {
        var status = _installationQueue.GetStatus(installationId);
        if (status is null)
        {
            return NotFound(new { message = "Installation not found" });
        }

        return Ok(ModelInstallationStatusDto.From(status));
    }

    [HttpGet("config")]
    public async Task<ActionResult> GetConfiguration(CancellationToken cancellationToken)
    {
        var config = await _catalogService.GetConfigurationAsync(cancellationToken);
        return Ok(config);
    }

    [HttpPut("text-model")]
    public async Task<ActionResult> SetTextModel([FromBody] ModelSelectionRequest request, CancellationToken cancellationToken)
    {
        await _catalogService.SetDefaultModelAsync("text", request.ModelName, request.Provider, cancellationToken);
        return NoContent();
    }

    [HttpPut("vision-model")]
    public async Task<ActionResult> SetVisionModel([FromBody] ModelSelectionRequest request, CancellationToken cancellationToken)
    {
        await _catalogService.SetDefaultModelAsync("vision", request.ModelName, request.Provider, cancellationToken);
        return NoContent();
    }

    [HttpGet("providers")]
    public async Task<ActionResult> GetProviders(CancellationToken cancellationToken)
    {
        var providers = await _catalogService.GetProvidersAsync(cancellationToken);
        return Ok(providers);
    }

    [HttpGet("health")]
    public async Task<ActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var health = await _catalogService.GetHealthAsync(cancellationToken);
        return Ok(health);
    }

    [HttpGet("usage-stats")]
    public async Task<ActionResult> GetUsage(CancellationToken cancellationToken)
    {
        var usage = await _catalogService.GetUsageAsync(cancellationToken);
        return Ok(usage);
    }
}

public class InstallModelRequest
{
    public string? Provider { get; set; }
}

public class ModelSelectionRequest
{
    public string ModelName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
}

public record ModelInstallationStatusDto
{
    public Guid InstallationId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int Progress { get; init; }
    public string CurrentStep { get; init; } = string.Empty;
    public DateTimeOffset EnqueuedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? LastUpdated { get; init; }
    public string? ErrorMessage { get; init; }

    public static ModelInstallationStatusDto From(ModelInstallationStatus status) => new()
    {
        InstallationId = status.InstallationId,
        Provider = status.Provider,
        ModelName = status.ModelName,
        State = status.State.ToString().ToLowerInvariant(),
        Progress = status.Progress,
        CurrentStep = status.CurrentStep,
        EnqueuedAt = status.EnqueuedAt,
        StartedAt = status.StartedAt,
        CompletedAt = status.CompletedAt,
        LastUpdated = status.LastUpdated,
        ErrorMessage = status.ErrorMessage
    };
}
