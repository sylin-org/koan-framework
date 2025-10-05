using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace S8.Canon.Api.Controllers;

public sealed class FlowSeedRequest
{
    public int Count { get; set; } = 10;
}

[ApiController]
[Route("api/flow/seed")]
public sealed class FlowSeedController : ControllerBase
{
    private readonly IFlowSeeder _seeder;
    public FlowSeedController(IFlowSeeder seeder) { _seeder = seeder; }

    [HttpPost]
    public async Task<IActionResult> Seed([FromBody] FlowSeedRequest req, CancellationToken ct)
    {
        var result = await _seeder.SeedAllAdaptersAsync(req.Count, ct);
        return Ok(result);
    }
}
