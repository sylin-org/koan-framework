using Microsoft.AspNetCore.Mvc;
using Sora.Web.Attributes;
using Sora.Web.Controllers;
using Sora.Flow.Model;
using S8.Flow.Shared;
using Sora.Data.Core;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/devices")]
[SoraDataBehavior(MustPaginate = true, DefaultPageSize = 20, MaxPageSize = 200)]
public sealed class DevicesController : EntityController<DynamicFlowEntity<Device>>
{
	// Resolve by CanonicalId (business key) → ULID; return 302 to the primary route
	[HttpGet("by-cid/{canonicalId}")]
	public async Task<IActionResult> GetByCanonicalId(string canonicalId, CancellationToken ct)
	{
		var refItem = await ReferenceItem<Device>.GetByCanonicalId(canonicalId, ct);
		if (refItem is null) return NotFound();
		return Redirect($"/api/devices/{refItem.Id}");
	}
}
