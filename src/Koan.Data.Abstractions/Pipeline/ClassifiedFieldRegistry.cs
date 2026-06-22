using System.Collections.Concurrent;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// The static Type-plane index of <see cref="ClassifiedPropertyBag"/>s plus the global activation off-gate
/// (ARCH-0098 §1). Mirrors <see cref="ManagedFieldRegistry"/>'s mechanics (Type-plane memo, an <see cref="IsEmpty"/>
/// volatile off-gate so the off path is byte-identical) but sources its FACTS from per-type entity scans rather
/// than module registration — the facts live on the entity (the <c>[Classified]</c> attributes), not in a
/// registered descriptor list.
///
/// <para><b>Off = structurally absent.</b> Until a handling module (<c>Koan.Classification</c>) calls
/// <see cref="Activate"/> (Reference = Intent), <see cref="IsEmpty"/> is <c>true</c> and <see cref="ForType"/>
/// returns the empty bag without scanning — the chokepoint short-circuits to its byte-identical pre-change path.
/// The FACTS may exist on entities, but with no handling registered there is nothing to transform, so the scan is
/// skipped entirely. Activation is a boot-only operation (registrars run before any data op).</para>
/// </summary>
public static class ClassifiedFieldRegistry
{
    private static readonly ConcurrentDictionary<Type, ClassifiedPropertyBag> _byType = new();
    private static readonly ClassifiedPropertyBag _empty = new(typeof(object));   // object declares no [Classified]
    private static volatile bool _isEmpty = true;

    /// <summary>Whether no handling module has activated classification — the hot-path off gate. Cheap volatile read.</summary>
    public static bool IsEmpty => _isEmpty;

    /// <summary>
    /// Activate classification handling (called once from the <c>Koan.Classification</c> registrar). Boot-only and
    /// idempotent. Flips the off-gate so the chokepoint begins scanning + transforming classified fields.
    /// </summary>
    public static void Activate() => _isEmpty = false;

    /// <summary>
    /// The classified-field bag for <paramref name="entityType"/>, Type-plane memoized. Returns the shared empty
    /// bag (no scan) while <see cref="IsEmpty"/> is <c>true</c>.
    /// </summary>
    public static ClassifiedPropertyBag ForType(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (_isEmpty) return _empty;
        return _byType.GetOrAdd(entityType, static t => new ClassifiedPropertyBag(t));
    }

    /// <summary>Test-support: deactivate and clear the memo (mirrors <c>ManagedFieldRegistry.Reset</c>).</summary>
    public static void Reset()
    {
        _isEmpty = true;
        _byType.Clear();
    }
}
