using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Koan.Data.Abstractions;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Koan.Web.Options;

namespace Koan.Web.Queries;

/// <summary>
/// Static helper for parsing HTTP query parameters into QueryOptions.
/// Pure function with no dependencies - all parameters passed explicitly.
/// Thread-safe by design with no mutable state.
/// </summary>
public static class EntityQueryParser
{
    /// <summary>
    /// Parses HTTP query collection into QueryOptions for entity queries.
    /// Supports: q (search), page, pageSize/size, sort, dir, output (shape), view.
    /// </summary>
    /// <param name="query">The HTTP query collection from Request.Query</param>
    /// <param name="defaults">Default options for pagination, view, and shape</param>
    /// <returns>Parsed QueryOptions with pagination, sorting, and shaping</returns>
    public static QueryOptions Parse(IQueryCollection query, EntityEndpointOptions defaults)
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

        // Sort specification (comma-separated, prefix with '-' for descending)
        if (query.TryGetValue("sort", out var vsort))
        {
            foreach (var spec in vsort.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var desc = spec.StartsWith('-');
                var field = desc ? spec[1..] : spec;
                if (!string.IsNullOrWhiteSpace(field))
                {
                    opts.Sort.Add(new SortSpec(field, desc));
                }
            }
        }

        // Direction override for single sort field
        if (query.TryGetValue("dir", out var vdir) && opts.Sort.Count == 1)
        {
            var direction = vdir.ToString();
            if (string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase))
            {
                opts.Sort[0] = new SortSpec(opts.Sort[0].Field, true);
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
}
