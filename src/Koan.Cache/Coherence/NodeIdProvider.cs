using System;

namespace Koan.Cache.Coherence;

/// <summary>
/// Per-process node identity. Generated once at construction. Used by the coordinator's
/// origin filter to drop the writer's own broadcasts. A fresh Guid on each process startup
/// is intentional — if a process restarts, its prior L1 is also gone, so missing a few
/// pre-restart messages doesn't matter.
/// </summary>
internal sealed class NodeIdProvider
{
    public Guid NodeId { get; } = Guid.NewGuid();
}
