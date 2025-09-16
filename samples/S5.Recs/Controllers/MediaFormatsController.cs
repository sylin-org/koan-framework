using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.MediaFormats)]
public class MediaFormatsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? mediaType = null, CancellationToken ct = default)
    {
        var mediaFormats = await MediaFormat.All(ct);
        var filtered = mediaFormats.AsEnumerable();

        // Filter by media type if specified
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var mediaTypes = await MediaType.All(ct);
            var targetType = mediaTypes.FirstOrDefault(mt => mt.Name.Equals(mediaType, StringComparison.OrdinalIgnoreCase));
            if (targetType != null)
            {
                filtered = filtered.Where(mf => mf.MediaTypeId == targetType.Id);
            }
        }

        var result = filtered.OrderBy(mf => mf.SortOrder).ThenBy(mf => mf.DisplayName).ToList();
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var mediaFormat = await MediaFormat.Get(id, ct);
        if (mediaFormat is null) return NotFound();
        return Ok(mediaFormat);
    }
}