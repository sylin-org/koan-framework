using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using S14.AdapterBench.Hubs;
using S14.AdapterBench.Models;
using S14.AdapterBench.Services;

namespace S14.AdapterBench.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BenchmarkController : ControllerBase
{
    private readonly IBenchmarkService _benchmarkService;
    private readonly IHubContext<BenchmarkHub> _hubContext;

    public BenchmarkController(
        IBenchmarkService benchmarkService,
        IHubContext<BenchmarkHub> hubContext)
    {
        _benchmarkService = benchmarkService;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Start a new benchmark run with the specified configuration.
    /// Progress updates are sent via SignalR to subscribed clients.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<BenchmarkResult>> RunBenchmark(
        [FromBody] BenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<BenchmarkProgress>(async p =>
        {
            await _hubContext.Clients.Group("BenchmarkProgress")
                .SendAsync("ProgressUpdate", p, cancellationToken);
        });

        var result = await _benchmarkService.RunBenchmarkAsync(request, progress, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get a list of available data providers for benchmarking.
    /// </summary>
    [HttpGet("providers")]
    public ActionResult<List<ProviderInfo>> GetProviders()
    {
        return Ok(new List<ProviderInfo>
        {
            new() { Name = "sqlite", DisplayName = "SQLite", IsContainerized = false, IsDefault = true },
            new() { Name = "postgres", DisplayName = "PostgreSQL", IsContainerized = true, IsDefault = true },
            new() { Name = "mongo", DisplayName = "MongoDB", IsContainerized = true, IsDefault = true },
            new() { Name = "redis", DisplayName = "Redis", IsContainerized = true, IsDefault = true }
        });
    }

    /// <summary>
    /// Get available entity tiers for benchmarking.
    /// </summary>
    [HttpGet("tiers")]
    public ActionResult<List<TierInfo>> GetTiers()
    {
        return Ok(new List<TierInfo>
        {
            new() { Name = "Minimal", Description = "ID + timestamp only", IsDefault = true },
            new() { Name = "Indexed", Description = "Business entity with indexed properties", IsDefault = true },
            new() { Name = "Complex", Description = "Complex document with nested objects", IsDefault = true }
        });
    }
}

public class ProviderInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsContainerized { get; set; }
    public bool IsDefault { get; set; }
}

public class TierInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsDefault { get; set; }
}
