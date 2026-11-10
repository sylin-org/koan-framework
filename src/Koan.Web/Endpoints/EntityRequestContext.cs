using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Koan.Core.Capabilities;
using Koan.Web.Hooks;

namespace Koan.Web.Endpoints;

/// <summary>
/// Protocol-neutral context describing an entity endpoint invocation.
/// Captures per-request services, options, headers, and metadata shared across REST, GraphQL, and future adapters.
/// </summary>
public sealed class EntityRequestContext
{
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
        Capabilities = new CapabilitySet();
    }

    public IServiceProvider Services { get; }

    public QueryOptions Options { get; }

    public CancellationToken CancellationToken { get; }

    public HttpContext? HttpContext { get; }

    public ClaimsPrincipal User { get; }

    /// <summary>
    /// True when the selected transport authentication handler rejected a supplied credential. This remains
    /// internal framework state: applications declare access normally, while the shared endpoint fails closed.
    /// </summary>
    internal bool AuthenticationRejected { get; init; }

    /// <summary>
    /// The backing provider's capabilities as the unified <see cref="CapabilitySet"/> (ARCH-0084).
    /// Populated by the endpoint from <c>DataCaps.Describe(repo)</c> — carries both query and write tokens.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Koan.Web.Authorization.FieldAccess? FieldAccess { get; private set; }

    /// <summary>Bind one adapter's stricter prepared field policy after the context establishes its principal.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void BindFieldAccess(Koan.Web.Authorization.FieldAccess fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (FieldAccess is not null || !fields.IsCurrent(fields.RootType, User))
            throw new InvalidOperationException("Field access must bind once to the effective request principal.");
        FieldAccess = fields;
    }

    /// <summary>The provider capabilities populated by the shared endpoint.</summary>
    public CapabilitySet Capabilities { get; set; }

    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IList<string> Warnings { get; } = new List<string>();

    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// <summary>The controller answering this request, which runs as a hook of its own requests where it implements one (WEB-0087); null on
    /// a door with no controller.</summary>
    internal object? Owner { get; set; }

    /// <summary>What the operation took beside the entity's members, bound from the request (WEB-0087), or null where it declares nothing.</summary>
    internal object? Input { get; set; }

    /// <summary>The operation this request serves (WEB-0089), set by the endpoint's entry point before its first hook and never after.</summary>
    internal EntityOperationKind Operation { get; set; }

    /// <summary>The sealed key a declared collection's next page continues from (DATA-0170), or null on its last page or any other read.</summary>
    internal string? Next { get; set; }

    public void Warn(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Warnings.Add(message);
        }
    }
}









