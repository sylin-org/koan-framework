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
        // Normalize the incoming stream to a MemoryStream to avoid provider quirks
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        ms.Position = 0;

        // Use a GUID-based key for collision-free addressing and to enable ID-first routes
        var id = Guid.NewGuid();
        var ext = System.IO.Path.GetExtension(file.FileName);
        var key = id.ToString("N") + (string.IsNullOrWhiteSpace(ext) ? string.Empty : ext.ToLowerInvariant());

        // Store the bytes under the GUID key; keep original filename in Name for clients/UI
        var obj = await ProfileMedia.Onboard(key, ms, file.ContentType, ct);
        // Manually set Name if not populated by provider
        if (string.IsNullOrWhiteSpace(obj.Name)) obj = ProfileMedia.Get(obj.Key, file.FileName);

        // Return both id and key so clients can use ID-based media routes or direct key access
        return Ok(new { id, key = obj.Key, provider = obj.Provider, container = obj.Container });
    }
}
