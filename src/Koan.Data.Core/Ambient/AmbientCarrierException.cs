using System;
using System.Collections.Generic;

namespace Koan.Data.Core;

/// <summary>
/// ARCH-0100: thrown by <see cref="AmbientCarrierRegistry.Restore"/> when a durable carrier bag names an ambient
/// axis that has <b>no registered carrier</b> on this host (the owning module is absent). This is the carrier's
/// one self-owned fail-closed decision: the captured axis cannot be rehydrated, so the work must not run
/// fail-open in a weaker ambient than it was submitted in. It is non-retryable (the carrier set is fixed at host
/// build) — the orchestrator maps it to an immediate dead-letter with this named reason.
/// </summary>
public sealed class AmbientCarrierException : InvalidOperationException
{
    /// <summary>The bag axis keys that had no registered carrier.</summary>
    public IReadOnlyCollection<string> UnregisteredAxes { get; }

    public AmbientCarrierException(IReadOnlyCollection<string> unregisteredAxes)
        : base($"Durable carrier names ambient axis/axes with no registered carrier on this host: "
             + $"{string.Join(", ", unregisteredAxes)}. Refusing to execute fail-open.")
        => UnregisteredAxes = unregisteredAxes;
}
