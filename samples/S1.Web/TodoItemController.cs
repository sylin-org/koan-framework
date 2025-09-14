using Microsoft.AspNetCore.Mvc;
using Sora.Data.Core;
using Sora.Web.Attributes;
using Sora.Web.Controllers;

namespace S1.Web;

[Route("api/todoitems")]
[SoraDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
public sealed class TodoItemController : EntityController<TodoItem>
{
    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var deleted = await TodoItem.RemoveAll(ct);
        return Ok(new { deleted });
    }
}