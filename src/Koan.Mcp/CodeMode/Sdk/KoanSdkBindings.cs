using System;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.CodeMode.Sdk;

/// <summary>
/// Root SDK object exposed to JavaScript as 'SDK'.
/// Provides access to Entities, Out, and other domains.
/// </summary>
public sealed class KoanSdkBindings
{
    private readonly IServiceProvider _services;

    public KoanSdkBindings(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Entities = new EntityDomain(services);
        Out = new OutputDomain();
        Metrics = new MetricsDomain();
    }

    /// <summary>
    /// SDK.Entities.* - Access to entity operations (collection, getById, upsert, delete).
    /// </summary>
    public EntityDomain Entities { get; }

    /// <summary>
    /// SDK.Out.* - Output and logging functions.
    /// </summary>
    public OutputDomain Out { get; }

    /// <summary>
    /// Internal metrics tracking.
    /// </summary>
    internal MetricsDomain Metrics { get; }
}
