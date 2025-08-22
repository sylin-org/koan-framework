using Microsoft.AspNetCore.Mvc;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Core.Model;
using Sora.Web.Attributes;
using Sora.Web.Controllers;

namespace S4.Web.Controllers;

[Route("api/items")]
[SoraDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
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

// GraphQL endpoint is provided by Sora.Web.GraphQl's centralized controller (/graphql).

[DataAdapter("mongo")]
public sealed class Item : Entity<Item>
{
    public string Name { get; set; } = string.Empty;
}
