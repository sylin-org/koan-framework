using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using Koan.Canon.Web.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Primitives;
using Koan.Web.Controllers;

namespace Koan.Canon.Web.Controllers;

/// <summary>
/// Canon-aware entity controller that routes writes through the canon runtime.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
public class CanonEntitiesController<TModel> : EntityController<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    private readonly ICanonRuntime _runtime;
    private readonly ICanonModelCatalog _catalog;

    public CanonEntitiesController(ICanonRuntime runtime, ICanonModelCatalog catalog)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        // Ensure a descriptor exists for this model so runtime routes remain deterministic.
        if (!_catalog.TryGetByType(typeof(TModel), out _))
        {
            throw new InvalidOperationException($"No canon model descriptor registered for type '{typeof(TModel).FullName}'.");
        }
    }

    [HttpPost("")]
    public override async Task<IActionResult> Upsert([FromBody][ValidateNever] TModel model, CancellationToken ct)
    {
        if (model is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        var options = BuildOptionsFromRequest();
        var result = await _runtime.Canonize(model, options, ct).ConfigureAwait(false);
        return Ok(CanonizationResponse<TModel>.FromResult(result));
    }

    [HttpPost("bulk")]
    public override async Task<IActionResult> UpsertMany([FromBody][ValidateNever] IEnumerable<TModel> models, CancellationToken ct)
    {
        if (models is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        var list = models.ToList();
        if (list.Count == 0)
        {
            return Ok(Array.Empty<CanonizationResponse<TModel>>());
        }

        var options = BuildOptionsFromRequest();
        var responses = new List<CanonizationResponse<TModel>>(list.Count);
        foreach (var model in list)
        {
            var result = await _runtime.Canonize(model, options, ct).ConfigureAwait(false);
            responses.Add(CanonizationResponse<TModel>.FromResult(result));
        }

        return Ok(responses);
    }

    private CanonizationOptions BuildOptionsFromRequest()
    {
        var options = CanonizationOptions.Default.Copy();
        var request = HttpContext?.Request;
        if (request is null)
        {
            return options;
        }

        var query = request.Query;

        if (TryGetHeaderOrQueryValue("X-Canon-Origin", "origin", out var originValue) && originValue is { Length: > 0 } origin)
        {
            options = options.WithOrigin(origin);
        }

        if (TryGetCorrelationId(out var correlationIdValue) && correlationIdValue is { Length: > 0 } correlationId)
        {
            options = options with { CorrelationId = correlationId };
        }

        if (TryGetBoolean(query, "forceRebuild", out var forceRebuild) && forceRebuild)
        {
            options = options with { ForceRebuild = true };
        }

        if (TryGetBoolean(query, "skipDistribution", out var skipDistribution) && skipDistribution)
        {
            options = options with { SkipDistribution = true };
        }

        if (query.TryGetValue("stageBehavior", out var stageBehaviorValue) && !StringValues.IsNullOrEmpty(stageBehaviorValue))
        {
            if (Enum.TryParse<CanonStageBehavior>(stageBehaviorValue.ToString(), ignoreCase: true, out var behavior))
            {
                options = options.WithStageBehavior(behavior);
            }
        }

    if (query.TryGetValue("views", out var viewsValue) && !StringValues.IsNullOrEmpty(viewsValue))
        {
            var views = viewsValue
        .SelectMany(static value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (views.Length > 0)
            {
                options = options.WithRequestedViews(views);
            }
        }

        #pragma warning disable CS8602 // Query keys and headers are populated by ASP.NET and never null for reachable entries.
        foreach (var kvp in query)
        {
            var rawKey = kvp.Key;
            if (string.IsNullOrEmpty(rawKey))
            {
                continue;
            }

            if (rawKey.StartsWith("tag.", StringComparison.OrdinalIgnoreCase))
            {
                var key = rawKey.Length > 4 ? rawKey[4..] : string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (TryGetTagValue(kvp.Value, out var tagValue))
                {
                    options = AddTagOption(options, key, tagValue);
                }
            }
        }

        if (request.Headers.TryGetValue("X-Canon-Tag", out var tagHeaders))
        {
            foreach (var tagHeader in tagHeaders.Where(static header => !string.IsNullOrWhiteSpace(header)))
            {
                var parts = tagHeader.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]))
                {
                    var tagKey = parts[0];
                    var tagValue = parts[1] ?? string.Empty;
                    options = options.WithTag(tagKey, tagValue);
                }
            }
        }

        foreach (var header in request.Headers)
        {
            var headerKey = header.Key;
            if (string.IsNullOrEmpty(headerKey))
            {
                continue;
            }

            if (headerKey.StartsWith("X-Canon-Tag-", StringComparison.OrdinalIgnoreCase))
            {
                var key = headerKey.Length > 12 ? headerKey[12..] : string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (TryGetTagValue(header.Value, out var headerValue))
                {
                    options = AddTagOption(options, key, headerValue);
                }
            }
        }
        #pragma warning restore CS8602

        return options;
    }

    private bool TryGetHeaderOrQueryValue(string headerName, string queryName, [NotNullWhen(true)] out string? value)
    {
        value = string.Empty;
        var context = HttpContext;
        if (context?.Request is not { } request)
        {
            return false;
        }

        if (request.Headers.TryGetValue(headerName, out var headerValue) && !StringValues.IsNullOrEmpty(headerValue))
        {
            value = headerValue.ToString();
            return true;
        }

        if (request.Query.TryGetValue(queryName, out var queryValue) && !StringValues.IsNullOrEmpty(queryValue))
        {
            value = queryValue.ToString();
            return true;
        }

        return false;
    }

    private bool TryGetCorrelationId([NotNullWhen(true)] out string? correlationId)
    {
        correlationId = string.Empty;
        var context = HttpContext;
        if (context?.Request is not { } request)
        {
            return false;
        }

        if (request.Headers.TryGetValue("X-Correlation-ID", out var primary) && !StringValues.IsNullOrEmpty(primary))
        {
            correlationId = primary.ToString();
            return true;
        }

        if (request.Headers.TryGetValue("X-Request-ID", out var fallback) && !StringValues.IsNullOrEmpty(fallback))
        {
            correlationId = fallback.ToString();
            return true;
        }

        if (!string.IsNullOrWhiteSpace(context.TraceIdentifier))
        {
            correlationId = context.TraceIdentifier;
            return true;
        }

        return false;
    }

    private static bool TryGetBoolean(IQueryCollection query, string key, out bool value)
    {
        value = default;
        if (query.TryGetValue(key, out var raw) && !StringValues.IsNullOrEmpty(raw))
        {
            return bool.TryParse(raw.ToString(), out value);
        }

        return false;
    }

    private static bool TryGetTagValue(StringValues source, [NotNullWhen(true)] out string? value)
    {
        if (StringValues.IsNullOrEmpty(source))
        {
            value = string.Empty;
            return false;
        }

        value = source.ToString();
        return !string.IsNullOrEmpty(value);
    }

    private static CanonizationOptions AddTagOption(CanonizationOptions options, string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return options.WithTag(key, value ?? string.Empty);
    }

    public sealed record CanonizationResponse<T>(
        T Canonical,
        CanonizationOutcome Outcome,
        CanonMetadata Metadata,
        IReadOnlyList<CanonizationEvent> Events,
        bool ReprojectionTriggered,
        bool DistributionSkipped)
        where T : CanonEntity<T>, new()
    {
        public static CanonizationResponse<T> FromResult(CanonizationResult<T> result)
            => new(
                result.Canonical,
                result.Outcome,
                result.Metadata,
                result.Events,
                result.ReprojectionTriggered,
                result.DistributionSkipped);
    }
}
