using Koan.Core.Naming;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// A generic, axis-agnostic seam that contributes a leading/trailing <see cref="Particle"/> to an entity's physical
/// storage identifier (ARCH-0101 §3 — the container-name particle plane). The <b>separate-container mode</b> of a
/// data-segmentation axis (e.g. tenancy: <c>T1-Todo</c>) registers one of these; <see cref="StorageNameGenerator"/>
/// folds it alongside the partition particle via the ONE ARCH-0096 <c>IdentifierComposer</c>. <b>"The axis is never in
/// the spine"</b> holds by construction: the spine is the <i>anchor</i> (<c>Todo</c>), untouched; the axis is a
/// separable particle around it. The data core never names the axis; no registered contributor ⇒ an empty fold ⇒ a
/// byte-identical name (structural absence; Reference = Intent).
///
/// <para>Registered into the static <see cref="StorageNameParticleRegistry"/> from a module's
/// <c>KoanAutoRegistrar</c>, not DI-enumerable — the same declared deviation <c>ManagedFieldRegistry</c> uses
/// (DATA-0105 §4): <see cref="StorageNameGenerator"/> is a static, cached composer reached deep in adapter naming
/// (data <i>and</i> vector) where no DI scope exists, and changing its signature would break every caller and miss
/// the cache key. <see cref="GetParticle"/> reads the current ambient axis value ONCE per name resolve and returns an
/// immutable particle (the composer owns ordering / position / separator / byte-clamp). MUST be cheap.</para>
/// </summary>
public interface IStorageNameParticleContributor
{
    /// <summary>
    /// The logical axis id this contributor owns (e.g. <c>"tenant"</c>) — ambient-independent, the registry's dedup
    /// key (mirrors <c>ManagedFieldDescriptor.StorageName</c>), and the label the boot report / <c>.Explain()</c> use.
    /// Conventionally equals the <see cref="Particle.Axis"/> of the particles it yields.
    /// </summary>
    string Axis { get; }

    /// <summary>
    /// The name particle for <paramref name="entityType"/> in the current ambient, or <c>null</c> when the axis
    /// imposes no container particle (off / host / not-applicable). The <see cref="Particle.Value"/> carries the
    /// ambient axis token; the composer fails closed (ARCH-0101 §8) if the value is not identifier-injective under the
    /// adapter's partition token policy (<see cref="PartitionTokenPolicy.IsInjective"/>) — a lossy value cannot silently
    /// collide two scopes into one physical container.
    /// </summary>
    Particle? GetParticle(System.Type entityType);
}
