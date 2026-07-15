using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Sorting;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Koan.Web.Options;

namespace Koan.Web.Queries;

/// <summary>
/// Static helper for parsing HTTP query parameters into <see cref="QueryOptions"/>.
/// Sort fields are resolved against the requested entity type at parse time
/// (strict by default — unresolvable fields throw <see cref="InvalidSortFieldException"/>).
/// </summary>
public static class EntityQueryParser
{
    /// <summary>
    /// Parses HTTP query collection into <see cref="QueryOptions"/> for entity queries.
    /// Supports: q (search), page, pageSize/size, sort, dir, output (shape), view.
    /// </summary>
    public static QueryOptions Parse<TEntity>(IQueryCollection query, EntityEndpointOptions defaults, bool lenient = false)
        => Parse(typeof(TEntity), query, defaults, lenient);

    /// <summary>
    /// Non-generic overload; useful for reflective callers (e.g. MCP RequestTranslator).
    /// </summary>
    public static QueryOptions Parse(Type entityType, IQueryCollection query, EntityEndpointOptions defaults, bool lenient = false)
    {
        var opts = new QueryOptions
        {
            Page = 1,
            PageSize = defaults.DefaultPageSize,
            View = defaults.DefaultView
        };

        // Search query
        if (query.TryGetValue("q", out var vq))
        {
            opts.Q = vq.FirstOrDefault();
        }

        // Page number (1-based)
        if (query.TryGetValue("page", out var vp) && int.TryParse(vp, out var page) && page > 0)
        {
            opts.Page = page;
        }

        // Page size (capped to max)
        var maxSize = defaults.MaxPageSize;
        if (query.TryGetValue("pageSize", out var vps) && int.TryParse(vps, out var requested) && requested > 0)
        {
            opts.PageSize = Math.Min(requested, maxSize);
        }
        else if (query.TryGetValue("size", out var vs) && int.TryParse(vs, out var size) && size > 0)
        {
            opts.PageSize = Math.Min(size, maxSize);
        }

        // Sort specification (comma-separated; prefix '-' for desc, '+' or none for asc)
        if (query.TryGetValue("sort", out var vsort))
        {
            var expr = vsort.ToString();
            if (lenient || ShouldUseLenientMode(query, defaults))
            {
                var result = SortSpecParser.ParseLenient(entityType, expr);
                opts.Sort.AddRange(result.Specs);
                if (result.SkippedFields.Count > 0)
                {
                    opts.Extras["__sort_skipped"] = string.Join(",", result.SkippedFields);
                }
            }
            else
            {
                opts.Sort.AddRange(SortSpecParser.ParseStrict(entityType, expr));
            }
        }

        // Direction override for single sort field (legacy ?dir=asc|desc; kept for compat)
        if (query.TryGetValue("dir", out var vdir) && opts.Sort.Count == 1)
        {
            var direction = vdir.ToString();
            if (string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase))
            {
                opts.Sort[0] = opts.Sort[0] with { Desc = true };
            }
        }

        // Output shape (content transformation)
        if (query.TryGetValue("output", out var vout))
        {
            var shape = vout.ToString();
            if (defaults.IsShapeAllowed(shape))
            {
                opts.Shape = shape;
            }
        }

        // View selection
        if (query.TryGetValue("view", out var vview))
        {
            opts.View = vview.ToString();
        }
        else if (string.IsNullOrWhiteSpace(opts.View))
        {
            opts.View = defaults.DefaultView;
        }

        return opts;
    }

    private static bool ShouldUseLenientMode(IQueryCollection query, EntityEndpointOptions defaults)
    {
        if (query.TryGetValue("ignoreUnknownSort", out var v) && bool.TryParse(v.ToString(), out var b) && b)
            return true;
        return defaults.LenientSort;
    }
}
