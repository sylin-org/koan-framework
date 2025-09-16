using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.MediaTypes)]
public class MediaTypesController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var mediaTypes = await MediaType.All(ct);
        var result = mediaTypes.OrderBy(mt => mt.SortOrder).ThenBy(mt => mt.DisplayName).ToList();
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var mediaType = await MediaType.Get(id, ct);
        if (mediaType is null) return NotFound();
        return Ok(mediaType);
    }
}