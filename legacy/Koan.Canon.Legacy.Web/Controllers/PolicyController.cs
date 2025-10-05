using Microsoft.AspNetCore.Mvc;
using Koan.Canon.Model;
using Koan.Data.Core;

namespace Koan.Canon.Web.Controllers;

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


