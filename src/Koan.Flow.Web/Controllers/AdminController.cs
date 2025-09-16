using Microsoft.AspNetCore.Mvc;
using Koan.Flow.Runtime;

namespace Koan.Flow.Web.Controllers;

[ApiController]
[Route("admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IFlowRuntime _runtime;
    public AdminController(IFlowRuntime runtime) => _runtime = runtime;

    public sealed record ReplayDto(DateTimeOffset? From, DateTimeOffset? Until);
    public sealed record ReprojectDto(string ReferenceId, string? ViewName = null);

    [HttpPost("replay")]
    public async Task<IActionResult> Replay([FromBody] ReplayDto dto, CancellationToken ct)
    {
        await _runtime.ReplayAsync(dto.From, dto.Until, ct);
        return Accepted();
    }

    [HttpPost("reproject")]
    public async Task<IActionResult> Reproject([FromBody] ReprojectDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ReferenceId)) return ValidationProblem("referenceId required");
        await _runtime.ReprojectAsync(dto.ReferenceId, dto.ViewName, ct);
        return Accepted();
    }
}
