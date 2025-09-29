using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using S9.Location.Core.Models;
using S9.Location.Core.Options;
using S9.Location.Core.Services;

namespace S9.Location.Api.Controllers;

[ApiController]
[Route("api/location")]
public class LocationController : ControllerBase
{
    private readonly ILocationMetricsService _metricsService;
    private readonly LocationOptions _options;

    public LocationController(ILocationMetricsService metricsService, IOptions<LocationOptions> options)
    {
        _metricsService = metricsService;
        _options = options.Value;
    }

    [HttpGet("canonical")]
    public async Task<ActionResult<IReadOnlyList<CanonicalLocation>>> GetCanonical([FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var page = await CanonicalLocation.FirstPage(pageSize, ct);
        return Ok(page);
    }

    [HttpGet("cache")]
    public async Task<ActionResult<IReadOnlyList<ResolutionCache>>> GetCache([FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var page = await ResolutionCache.FirstPage(pageSize, ct);
        return Ok(page);
    }

    [HttpGet("parked")]
    public async Task<ActionResult<IReadOnlyList<ParkedRecord<RawLocation>>>> GetParked([FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Parked)))
        {
            var records = await ParkedRecord<RawLocation>.FirstPage(pageSize, ct);
            return Ok(records);
        }
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<LocationMetrics>> GetMetrics(CancellationToken ct = default)
    {
        var snapshot = await _metricsService.GetSummaryAsync(ct);
        return Ok(snapshot);
    }

    [HttpGet("options")]
    public ActionResult GetOptions()
    {
        return Ok(new
        {
            DefaultCountry = _options.Normalization.DefaultCountry,
            AiAssistEnabled = _options.AiAssist.Enabled,
            AiAssistModel = _options.AiAssist.Model,
            AiConfidenceThreshold = _options.AiAssist.ConfidenceThreshold,
            CacheEnabled = _options.Cache.Enabled
        });
    }
}
