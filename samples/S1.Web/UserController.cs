using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace S1.Web;

[Route("api/users")]
[KoanDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
public sealed class UserController : EntityController<User>
{
    [HttpPost("seed/{count}")]
    public async Task<IActionResult> Seed([FromRoute] int count, CancellationToken ct)
    {
        var items = Enumerable
            .Range(0, Math.Clamp(count, 1, 100))
            .Select(i => new User
            {
                Name = $"User {i + 1}",
                Email = $"user{i + 1}@example.com"
            });

        var upserted = await items.Save(ct);
        return Ok(new { seeded = upserted });
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var deleted = await S1.Web.User.RemoveAll(ct);
        return Ok(new { deleted });
    }
}