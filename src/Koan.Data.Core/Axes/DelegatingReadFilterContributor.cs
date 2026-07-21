using System;
using System.Collections.Concurrent;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Pipeline;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The generic <see cref="IReadFilterContributor"/> a <see cref="Axis.Reads"/> declaration expands to (ARCH-0101 §7).
/// It reproduces a hand-written non-equality read contributor (e.g. <c>SoftDeleteReadContributor</c>) EXACTLY:
/// <c>ReadFilter(t) = Applies(t) ? predicate(t) : null</c>, a hard-bound isolation <see cref="RequiredCapability"/>,
/// and <c>ExcludesFromCache(t) = Applies(t)</c> (a viewer-context predicate cannot be an equality cache key, DATA-0106 §5).
///
/// <para>The <see cref="Axis.AppliesTo"/> decision is <b>ambient-independent</b> (a stable type→bool), so it is
/// memoized per type — honouring the <see cref="IReadFilterContributor.ReadFilter"/> contract ("Must be cheap … cache
/// per-type metadata") at the hot read plane, mirroring <c>ManagedFieldRegistry.ForType</c>. The predicate itself is
/// ambient-dependent (it reads the live scope) and is therefore evaluated each call, never cached.</para>
///
/// <para>Registered with a plain <c>services.Add(Singleton&lt;IReadFilterContributor&gt;(instance))</c> — NOT
/// <c>TryAddEnumerable</c>, which dedups by implementation type and would collapse every axis's contributor into one
/// (a silent read-scope hole when two predicate axes are active). See <c>DataAxisExpander</c>.</para>
/// </summary>
internal sealed class DelegatingReadFilterContributor : IReadFilterContributor
{
    private readonly Func<Type, bool> _appliesTo;
    private readonly Func<Type, Filter?> _predicate;
    private readonly Capability? _capability;
    private readonly ConcurrentDictionary<Type, bool> _applies = new();

    /// <summary>The logical axis id — for diagnostics and the boot-report / <c>.Explain</c> projection (Phase E).</summary>
    public string AxisId { get; }

    public DelegatingReadFilterContributor(string axisId, Func<Type, bool> appliesTo, Func<Type, Filter?> predicate, Capability? capability)
    {
        AxisId = axisId ?? throw new ArgumentNullException(nameof(axisId));
        _appliesTo = appliesTo ?? throw new ArgumentNullException(nameof(appliesTo));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _capability = capability;
    }

    // Type-plane memo: AppliesTo is ambient-independent ⇒ a safe forever-cache (no closure alloc per call).
    private bool Applies(Type entityType) => _applies.GetOrAdd(entityType, static (t, f) => f(t), _appliesTo);

    public Filter? ReadFilter(Type entityType) => Applies(entityType) ? _predicate(entityType) : null;

    public Capability? RequiredCapability => _capability;

    public bool ExcludesFromCache(Type entityType) => Applies(entityType);
}
