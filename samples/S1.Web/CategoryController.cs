using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace S1.Web;

[Route("api/categories")]
[KoanDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
public sealed class CategoryController : EntityController<Category>
{
    [HttpPost("seed")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var categories = new[]
        {
            new Category { Name = "Work", Description = "Work-related tasks" },
            new Category { Name = "Personal", Description = "Personal tasks and goals" },
            new Category { Name = "Shopping", Description = "Shopping lists and errands" },
            new Category { Name = "Health", Description = "Health and fitness goals" },
            new Category { Name = "Learning", Description = "Educational and skill development" }
        };

        var upserted = await categories.Save(ct);
        return Ok(new { seeded = upserted });
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var deleted = await Category.RemoveAll(ct);
        return Ok(new { deleted });
    }
}