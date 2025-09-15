using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Users)]
public class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var users = await UserDoc.All(ct);
        if (users.Count == 0)
        {
            var def = new UserDoc { Name = "Default User", IsDefault = true, CreatedAt = DateTimeOffset.UtcNow };
            await UserDoc.UpsertMany(new[] { def }, ct);
            users = await UserDoc.All(ct);
        }
        return Ok(users.Select(u => new { u.Id, u.Name, u.IsDefault }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        var u = new UserDoc { Name = req.Name, IsDefault = false, CreatedAt = DateTimeOffset.UtcNow };
        await UserDoc.UpsertMany(new[] { u }, ct);
        return Ok(new { u.Id, u.Name, u.IsDefault });
    }

    [HttpGet("{id}/stats")]
    public async Task<IActionResult> Stats([FromRoute] string id, CancellationToken ct)
    {
        var all = (await LibraryEntry.All(ct)).Where(e => e.UserId == id).ToList();
        var favorites = all.Count(e => e.Favorite);
        var completed = all.Count(e => e.Status == MediaStatus.Completed);
        var dropped = all.Count(e => e.Status == MediaStatus.Dropped);
        return Ok(new { favorites, completed, dropped });
    }
}
