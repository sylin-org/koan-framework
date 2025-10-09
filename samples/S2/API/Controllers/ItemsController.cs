using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace S2.Api.Controllers;

[Route("api/items")]
[Koan.Web.Transformers.EnableEntityTransformers]
[KoanDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
public sealed class ItemsController : EntityController<Item>
{
    [HttpPost("seed/{count}")]
    public async Task<IActionResult> Seed([FromRoute] int count, CancellationToken ct)
    {
        var seeded = await Enumerable.Range(0, Math.Clamp(count, 1, 1000))
            .Select(_ => new Item { Name = $"Item {Guid.NewGuid():N}" })
            .Save(ct);
        return Ok(new { seeded });
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var deleted = await Item.RemoveAll(ct);
        return Ok(new { deleted });
    }
}