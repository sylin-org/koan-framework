using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Sora.Media.Core.Model;
using Sora.Media.Core.Operators;
using Sora.Media.Core.Options;
using Sora.Media.Web.Controllers;
using Sora.Media.Web.Infrastructure;
using Sora.Storage;
using Sora.Storage.Model;
using SignatureUtility = Sora.Media.Core.Operators.SignatureUtility;
using HttpHeaderNames = Sora.Media.Web.Infrastructure.HttpHeaderNames;

namespace S6.SocialCreator.Controllers;

[ApiController]
[Route("api/media")]
public sealed class MediaController : MediaContentController<ProfileMedia>
{
	private readonly IMediaOperatorRegistry _registry;
	private readonly IStorageService _storage;
	private readonly IOptionsMonitor<MediaTransformOptions> _options;

	public MediaController(IMediaOperatorRegistry registry, IStorageService storage, IOptionsMonitor<MediaTransformOptions> options)
	{
		_registry = registry;
		_storage = storage;
		_options = options;
	}

	// ID-based route with optional filename flair
	[HttpGet("{id:guid}/{*filename}")]
	public async Task<IActionResult> GetById(Guid id, string? filename, CancellationToken ct)
	{
		var key = id.ToString("N");
		var stat = await StorageEntity<ProfileMedia>.Head(key, ct);
		if (stat is null) return NotFound();

		// Primitive-only: if this is already a variant (DerivationKey), skip transforms
		// For MVP, assume originals live under base container w/o variants marker

		// Resolve operators from query
		var ops = _registry.ResolveOperators(HttpContext.Request.Query.ToDictionary(k => k.Key, v => v.Value), stat, stat.ContentType, _options.CurrentValue);
		if (ops.Count == 0)
		{
			// No transforms: delegate to base by key
			return await base.Get(key, ct);
		}

		// Canonical signature
	(string hash, string json) = SignatureUtility.BuildSignature(key, stat, ops);
		var ext = System.IO.Path.GetExtension(filename ?? string.Empty);
		var flairName = string.IsNullOrWhiteSpace(filename) ? (id.ToString("N") + (string.IsNullOrWhiteSpace(ext) ? string.Empty : ext)) : filename!;

		// Variant storage path
		var variantKey = $"variants/{key}/{hash}/{flairName}";

		// Short-circuit: if exists, redirect to canonical id path which is the storage key
		var exists = await _storage.ExistsAsync("", "media", variantKey, ct);
		if (exists)
		{
			Response.Headers[HttpHeaderNames.XMediaVariant] = hash;
			var redirectUrl = Url.ActionLink(action: nameof(Get), controller: null, values: new { key = variantKey }, protocol: Request.Scheme, host: Request.Host.ToString());
			return RedirectPermanent(redirectUrl!);
		}

		// Create variant on-the-fly
		await using var src = await StorageEntity<ProfileMedia>.OpenRead(key, ct);
		await using var temp = new MemoryStream();

		// Execute pipeline serially
		string contentType = stat.ContentType ?? "application/octet-stream";
		Stream current = src;
		foreach (var tuple in ops)
		{
			var op = tuple.Op;
			var pars = tuple.Params;
			var next = new MemoryStream();
			var result = await op.Execute(current, contentType, next, pars, _options.CurrentValue, ct);
			next.Position = 0;
			if (!ReferenceEquals(current, src)) current.Dispose();
			current = next;
			if (result.ContentType is { } ctNew) contentType = ctNew;
		}

		// Persist
		await _storage.PutAsync("", "media", variantKey, current, contentType, ct);

		Response.Headers[HttpHeaderNames.XMediaVariant] = hash;
		var redirect = Url.ActionLink(action: nameof(Get), controller: null, values: new { key = variantKey }, protocol: Request.Scheme, host: Request.Host.ToString());
		return RedirectPermanent(redirect!);
	}
}
