using Microsoft.AspNetCore.Mvc;
using Sora.Flow.Model;
using Sora.Data.Core;

namespace Sora.Flow.Web.Controllers;

[ApiController]
[Route("policies")]
public sealed class PolicyController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await PolicyBundle.All(ct));

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] PolicyBundle model, CancellationToken ct)
    {
        await model.Save(ct);
        return Ok(model);
    }
}
