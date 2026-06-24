using System;
using System.Collections.Concurrent;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The generic <see cref="IStorageNameParticleContributor"/> a <see cref="AxisMode.Container"/> axis expands to
/// (ARCH-0101 §3/§7). It contributes a single <b>leading</b> particle (<c>T1-Todo</c>, order 100, separator <c>"-"</c>)
/// around the anchor when the axis applies and a value is in scope — byte-identical to a hand-written container
/// contributor. The anchor (spine) is untouched; <c>StorageNameGenerator</c> owns ordering / injectivity fail-closed /
/// the cache key.
///
/// <para><see cref="Axis.AppliesTo"/> is ambient-independent and memoized per type (hot-path discipline). The value
/// provider is ambient-dependent and read live per name resolve; a <c>null</c>/empty value yields no particle (off /
/// host ⇒ byte-identical name).</para>
/// </summary>
internal sealed class DelegatingNameParticleContributor : IStorageNameParticleContributor
{
    private const int LeadingOrder = 100;     // leads the partition (order 0); matches the ARCH-0101 §3 T1-Todo shape
    private const string Separator = "-";

    private readonly Func<Type, bool> _appliesTo;
    private readonly Func<object?> _valueProvider;
    private readonly ConcurrentDictionary<Type, bool> _applies = new();

    public string Axis { get; }

    public DelegatingNameParticleContributor(string axis, Func<Type, bool> appliesTo, Func<object?> valueProvider)
    {
        Axis = axis ?? throw new ArgumentNullException(nameof(axis));
        _appliesTo = appliesTo ?? throw new ArgumentNullException(nameof(appliesTo));
        _valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
    }

    private bool Applies(Type entityType) => _applies.GetOrAdd(entityType, static (t, f) => f(t), _appliesTo);

    public Particle? GetParticle(Type entityType)
    {
        if (!Applies(entityType)) return null;
        var value = _valueProvider();
        if (value is null) return null;                                  // off / host ⇒ byte-identical name
        // Fail closed on a non-string value (ARCH-0101 §8): a container token is a string identifier. A non-string
        // value would collapse via ToString() with no injectivity parity — two distinct values whose ToString agrees
        // would merge into ONE physical container (a cross-scope leak the raw-value cache key would hide). Require a
        // string token explicitly (the StorageNameGenerator injectivity guard then catches lossy strings).
        if (value is not string token)
            throw new InvalidOperationException(
                $"Container-mode axis '{Axis}' produced a {value.GetType().Name} value; a container token must be a string. " +
                "Convert the value to a string in the axis .Field(...) value provider.");
        if (token.Length == 0) return null;                              // empty token ⇒ no particle
        return new Particle(LeadingOrder, Axis, token, ParticlePosition.Leading, Separator);
    }
}
