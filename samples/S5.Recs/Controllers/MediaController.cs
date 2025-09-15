using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Media)]
public class MediaController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var media = await Media.Get(id, ct);
        if (media is null) return NotFound();
        return Ok(media);
    }

    [HttpGet("by-ids")]
    public async Task<IActionResult> GetByIds([FromQuery] string ids, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ids)) return Ok(Array.Empty<Media>());

        var list = new List<Media>();
        foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var media = await Media.Get(id, ct);
            if (media != null) list.Add(media);
        }
        return Ok(list);
    }

    [HttpGet]
    public async Task<IActionResult> GetFiltered(
        [FromQuery] string? type = null,
        [FromQuery] string? format = null,
        [FromQuery] string? genre = null,
        [FromQuery] int? limit = null,
        CancellationToken ct = default)
    {
        var allMedia = await Media.All(ct);
        var filtered = allMedia.AsEnumerable();

        // Filter by media type
        if (!string.IsNullOrWhiteSpace(type))
        {
            var mediaTypes = await MediaType.All(ct);
            var targetType = mediaTypes.FirstOrDefault(mt => mt.Name.Equals(type, StringComparison.OrdinalIgnoreCase));
            if (targetType != null)
            {
                filtered = filtered.Where(m => m.MediaTypeId == targetType.Id);
            }
        }

        // Filter by format
        if (!string.IsNullOrWhiteSpace(format))
        {
            var mediaFormats = await MediaFormat.All(ct);
            var targetFormat = mediaFormats.FirstOrDefault(mf => mf.Name.Equals(format, StringComparison.OrdinalIgnoreCase));
            if (targetFormat != null)
            {
                filtered = filtered.Where(m => m.MediaFormatId == targetFormat.Id);
            }
        }

        // Filter by genre
        if (!string.IsNullOrWhiteSpace(genre))
        {
            filtered = filtered.Where(m => (m.Genres ?? Array.Empty<string>()).Contains(genre, StringComparer.OrdinalIgnoreCase));
        }

        // Apply limit
        if (limit.HasValue && limit.Value > 0)
        {
            filtered = filtered.Take(limit.Value);
        }

        var result = filtered.OrderByDescending(m => m.Popularity).ToList();
        return Ok(result);
    }

    [HttpGet("resolve")]
    public async Task<IActionResult> ResolveByExternal(
        [FromQuery] string provider,
        [FromQuery] string externalId,
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(externalId))
        {
            return BadRequest("Provider and externalId are required");
        }

        MediaType? mediaType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            var mediaTypes = await MediaType.All(ct);
            mediaType = mediaTypes.FirstOrDefault(mt => mt.Name.Equals(type, StringComparison.OrdinalIgnoreCase));
            if (mediaType == null)
            {
                return BadRequest($"Media type '{type}' not found");
            }
        }

        // If no type specified, search across all types
        if (mediaType != null)
        {
            var id = Media.MakeId(provider, externalId, mediaType.Id!);
            var media = await Media.Get(id, ct);
            if (media != null) return Ok(media);
        }
        else
        {
            // Search across all media types
            var mediaTypes = await MediaType.All(ct);
            foreach (var mt in mediaTypes)
            {
                var id = Media.MakeId(provider, externalId, mt.Id!);
                var media = await Media.Get(id, ct);
                if (media != null) return Ok(media);
            }
        }

        return NotFound();
    }
}