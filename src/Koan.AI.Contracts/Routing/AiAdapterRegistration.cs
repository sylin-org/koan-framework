using System;
using Koan.AI.Contracts.Adapters;

namespace Koan.AI.Contracts.Routing;

/// <summary>
/// Materialized metadata stored in the adapter registry for election decisions.
/// </summary>
public sealed record AiAdapterRegistration
{
    public required IAiAdapter Adapter { get; init; }
    public required int Priority { get; init; }
    public required int Weight { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
}
