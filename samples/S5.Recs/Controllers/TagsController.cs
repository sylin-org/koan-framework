using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;
using Microsoft.Extensions.Options;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Tags)]
public class TagsController : ControllerBase
{
    private static bool IsCensored(string tag, string[]? censor)
        => !string.IsNullOrWhiteSpace(tag) &&
           (censor?.Any(c => !string.IsNullOrWhiteSpace(c) && tag.Equals(c, StringComparison.OrdinalIgnoreCase)) ?? false);

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? sort = "popularity", [FromQuery] int? top = null, [FromServices] IOptions<S5.Recs.Options.TagCatalogOptions>? tagOptions = null, CancellationToken ct = default)
    {
        var list = await TagStatDoc.All(ct);
        var opt = tagOptions?.Value?.CensorTags ?? Array.Empty<string>();
        var doc = await S5.Recs.Models.CensorTagsDoc.Get("recs:censor-tags", ct);
        var dyn = doc?.Tags?.ToArray() ?? Array.Empty<string>();
        var censor = opt.Concat(dyn).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IEnumerable<TagStatDoc> q = list.Where(t => !IsCensored(t.Tag, censor));
        if (string.Equals(sort, "alpha", StringComparison.OrdinalIgnoreCase) || string.Equals(sort, "name", StringComparison.OrdinalIgnoreCase))
            q = q.OrderBy(t => t.Tag);
        else
            q = q.OrderByDescending(t => t.AnimeCount).ThenBy(t => t.Tag);
        if (top.HasValue && top.Value > 0) q = q.Take(top.Value);
        return Ok(q.Select(t => new { tag = t.Tag, count = t.AnimeCount }));
    }
}
