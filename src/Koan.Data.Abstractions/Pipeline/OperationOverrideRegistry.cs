using System.Linq;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// The boot-time index of <see cref="OperationOverrideDescriptor"/>s (ARCH-0101 §4 — the operation-semantics override
/// plane). A cross-cutting module (e.g. <c>Koan.Data.SoftDelete</c>) registers its override from its
/// <c>KoanModule</c> (Reference = Intent); the data core's <c>RepositoryFacade</c> reads it generically at the
/// delete chokepoint and never names the axis.
///
/// <para>Deliberate static index (not DI) — the same declared deviation as <c>ManagedFieldRegistry</c> / DATA-0105 §4.
/// <b>Off = structurally absent:</b> when no module registers, <see cref="IsEmpty"/> is <c>true</c> and the facade's
/// delete paths are byte-identical (physical remove).</para>
/// </summary>
public static class OperationOverrideRegistry
{
    private static readonly object _gate = new();
    private static readonly List<OperationOverrideDescriptor> _descriptors = new();
    private static volatile bool _isEmpty = true;

    /// <summary>Whether no override is registered — the hot-path off gate. Cheap volatile read.</summary>
    public static bool IsEmpty => _isEmpty;

    /// <summary>Register an operation override. Boot-only. Idempotent by <see cref="OperationOverrideDescriptor.Field"/>.</summary>
    public static void Register(OperationOverrideDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_gate)
        {
            if (_descriptors.Any(d => string.Equals(d.Field, descriptor.Field, StringComparison.Ordinal))) return;
            _descriptors.Add(descriptor);
            _isEmpty = false;
        }
    }

    /// <summary>
    /// The single delete override governing <paramref name="entityType"/>, or <c>null</c> when none applies. (Soft-delete
    /// registers one; a second delete-override on the same type would be a misconfiguration — the first wins.)
    /// </summary>
    public static OperationOverrideDescriptor? ForDelete(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (_isEmpty) return null;
        lock (_gate)
        {
            foreach (var d in _descriptors)
                if (d.AppliesTo(entityType)) return d;
        }
        return null;
    }

    /// <summary>Test-support: clear all registrations (mirrors <c>ManagedFieldRegistry.Reset</c>).</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            _descriptors.Clear();
            _isEmpty = true;
        }
    }
}
