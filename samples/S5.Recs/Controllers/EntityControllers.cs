using Microsoft.AspNetCore.Mvc;
using S5.Recs.Models;
using Sora.Web;
using Sora.Web.Controllers;

namespace S5.Recs.Controllers;

[ApiController]
[Route("api/data/animes")]
public class AnimeDocController : EntityController<AnimeDoc, string> { }

[ApiController]
[Route("api/data/library")]
public class LibraryEntryDocController : EntityController<LibraryEntryDoc, string>
{
	[HttpGet("_whoami")] // GET /api/data/library/_whoami
	public IActionResult WhoAmI() => Ok(new
	{
		Controller = GetType().FullName,
		BaseType = GetType().BaseType?.FullName,
		Assembly = GetType().Assembly.FullName,
		Timestamp = DateTimeOffset.UtcNow
	});

	// Only one DebugHeaders endpoint should exist. If ambiguous, remove duplicates in other files.
	[HttpGet("_debugheaders")]
	public new IActionResult DebugHeaders()
	{
		var headers = new Dictionary<string, string>();
		foreach (var h in Request.Headers)
			headers[h.Key] = h.Value.ToString();
		var query = new Dictionary<string, string>();
		foreach (var q in Request.Query)
			query[q.Key] = q.Value.ToString();
		return Ok(new
		{
			Headers = headers,
			Query = query,
			Path = Request.Path,
			Method = Request.Method,
			Timestamp = DateTimeOffset.UtcNow
		});
	}

	public override async Task<IActionResult> GetCollection(CancellationToken ct)
	{
	return await base.GetCollection(ct);
	}
}

[ApiController]
[Route("api/data/users")]
public class UserDocController : EntityController<UserDoc, string> { }

[ApiController]
[Route("api/data/profiles")]
public class UserProfileDocController : EntityController<UserProfileDoc, string> { }

[ApiController]
[Route("api/data/genres")]
public class GenreStatDocController : EntityController<GenreStatDoc, string> { }

[ApiController]
[Route("api/data/tags")]
public class TagStatDocController : EntityController<TagStatDoc, string> { }

[ApiController]
[Route("api/data/censoredtags")]
public class CensorTagsDocController : EntityController<CensorTagsDoc, string> { }

[ApiController]
[Route("api/data/settings")]
public class SettingsDocController : EntityController<SettingsDoc, string> { }
