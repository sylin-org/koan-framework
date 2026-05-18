using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers.Tests.Fixtures;

/// <summary>Enricher with no predicate — always runs when registered.</summary>
public sealed class AlwaysEnricher : IEntityEnricher<Widget>
{
    public Task<Widget> Enrich(Widget model, HttpContext context)
        => Task.FromResult(model with { Enriched = true });

    public Task<IReadOnlyList<Widget>> EnrichMany(IReadOnlyList<Widget> models, HttpContext context)
        => Task.FromResult<IReadOnlyList<Widget>>(models.Select(m => m with { Enriched = true }).ToArray());
}

/// <summary>Enricher gated by a header — only runs when the request carries <c>X-Activate-Enrich: true</c>.</summary>
public sealed class HeaderGatedEnricher : IEntityEnricher<Widget>, ITransformerActivationPredicate
{
    public bool ShouldActivate(HttpContext context)
        => context.Request.Headers.TryGetValue("X-Activate-Enrich", out var v) && v.ToString() == "true";

    public Task<Widget> Enrich(Widget model, HttpContext context)
        => Task.FromResult(model with { Enriched = true });

    public Task<IReadOnlyList<Widget>> EnrichMany(IReadOnlyList<Widget> models, HttpContext context)
        => Task.FromResult<IReadOnlyList<Widget>>(models.Select(m => m with { Enriched = true }).ToArray());
}

/// <summary>Second enricher that stacks with the first — proves composition. Gated by a different header.</summary>
public sealed class AdminTagEnricher : IEntityEnricher<Widget>, ITransformerActivationPredicate
{
    public bool ShouldActivate(HttpContext context)
        => context.Request.Headers.TryGetValue("X-Activate-Admin", out var v) && v.ToString() == "true";

    public Task<Widget> Enrich(Widget model, HttpContext context)
        => Task.FromResult(model with { AdminTagged = true });

    public Task<IReadOnlyList<Widget>> EnrichMany(IReadOnlyList<Widget> models, HttpContext context)
        => Task.FromResult<IReadOnlyList<Widget>>(models.Select(m => m with { AdminTagged = true }).ToArray());
}
