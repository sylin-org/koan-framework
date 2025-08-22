using Microsoft.AspNetCore.Mvc;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core;
using Sora.Data.Core.Model;
using Sora.Web.Attributes;
using Sora.Web.Controllers;

namespace S1.Web;

[Route("api/todo")]
[SoraDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
public sealed class TodoController : EntityController<Todo>
{
    [HttpPost("seed/{count}")]
    public async Task<IActionResult> Seed([FromRoute] int count, CancellationToken ct)
    {
        var items = Enumerable
        .Range(0, Math.Clamp(count, 1, 1000))
        .Select(_ => new Todo { Title = $"Task {Guid.NewGuid():N}" });

        var upserted = await items.Save(ct);
        return Ok(new { seeded = upserted });
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var deleted = await Todo.RemoveAll(ct);
        return Ok(new { deleted });
    }
}

// Choose provider: default sqlite; to try json, replace with [DataAdapter("json")]
[DataAdapter("sqlite")]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
}
