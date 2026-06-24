using System;
using System.Collections.Generic;

namespace Koan.Data.Core.Axes;

/// <summary>One confirmed data-axis isolation leak found by the boot pre-flight (ARCH-0101 §8): a boot-active
/// read-scoped entity whose adapter cannot enforce the isolation.</summary>
/// <param name="Entity">The entity type that is read-scoped but unprotected.</param>
/// <param name="Adapter">The inner adapter type name that cannot isolate it.</param>
/// <param name="Reason">The actionable fix-it message (from the facade's fail-closed inspection).</param>
public sealed record DataAxisLeak(Type Entity, string Adapter, string Reason);

/// <summary>
/// Thrown at boot in <b>Production</b> (ARCH-0101 §8) when the pre-flight finds an entity read-scoped by an axis its
/// adapter cannot enforce — a leak that would silently return cross-scope rows (soft-deleted rows stay visible,
/// cross-tenant reads succeed). In Development the same finding is a loud warning and boot continues. Carries the full
/// list so the operator sees every offender and its fix.
/// </summary>
public sealed class DataAxisLeakException(IReadOnlyList<DataAxisLeak> leaks, string message) : Exception(message)
{
    public IReadOnlyList<DataAxisLeak> Leaks { get; } = leaks;
}
