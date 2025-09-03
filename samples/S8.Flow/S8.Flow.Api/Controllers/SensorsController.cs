using Microsoft.AspNetCore.Mvc;
using Sora.Web.Attributes;
using Sora.Web.Controllers;
using Sora.Flow.Model;
using S8.Flow.Shared;
using Sora.Data.Core;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/sensors")]
[SoraDataBehavior(MustPaginate = true, DefaultPageSize = 20, MaxPageSize = 200)]
public sealed class SensorsController : EntityController<DynamicFlowEntity<Sensor>>
{
	[HttpGet("by-cid/{canonicalId}")]
	public async Task<IActionResult> GetByCanonicalId(string canonicalId, CancellationToken ct)
	{
		var refItem = await ReferenceItem<Sensor>.GetByCanonicalId(canonicalId, ct);
		if (refItem is null) return NotFound();
		return Redirect($"/api/sensors/{refItem.Id}");
	}
}
