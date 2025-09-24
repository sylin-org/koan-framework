using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Koan.Data.Abstractions;
using Koan.Web.Hooks;

namespace Koan.Web.Endpoints;

/// <summary>
/// Protocol-neutral context describing an entity endpoint invocation.
/// Captures per-request services, options, headers, and metadata shared across REST, GraphQL, and future adapters.
/// </summary>
public sealed class EntityRequestContext
{
    private static readonly IQueryCapabilities EmptyCapabilities = new DefaultQueryCapabilities();

    public EntityRequestContext(
        IServiceProvider services,
        QueryOptions options,
        CancellationToken cancellationToken,
        HttpContext? httpContext = null,
        ClaimsPrincipal? user = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        CancellationToken = cancellationToken;
        HttpContext = httpContext;
        User = user ?? httpContext?.User ?? new ClaimsPrincipal();
        Capabilities = EmptyCapabilities;
    }

    public IServiceProvider Services { get; }

    public QueryOptions Options { get; }

    public CancellationToken CancellationToken { get; }

    public HttpContext? HttpContext { get; }

    public ClaimsPrincipal User { get; }

    public IQueryCapabilities Capabilities { get; set; }

    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IList<string> Warnings { get; } = new List<string>();

    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    public void Warn(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Warnings.Add(message);
        }
    }

    private sealed class DefaultQueryCapabilities : IQueryCapabilities
    {
        public QueryCapabilities Capabilities => QueryCapabilities.None;
    }
}









