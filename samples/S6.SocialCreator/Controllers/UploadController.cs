using Microsoft.AspNetCore.Mvc;
using Sora.Core;
using Sora.Media.Core.Model;
using Sora.Storage;

namespace S6.SocialCreator.Controllers;

[ApiController]
[Route("api/upload")]
public sealed class UploadController : ControllerBase
{
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("File required");
        await using var stream = file.OpenReadStream();
        var obj = await ProfileMedia.Upload(stream, file.FileName, file.ContentType, ct: ct);

    // TODO: start thumbnail task via Media pipeline when available; return both id and key for clients
    return Ok(new { id = obj.Id, key = obj.Key, provider = obj.Provider, container = obj.Container });
    }
}
