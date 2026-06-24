using System.Collections.Generic;

namespace Koan.Core.Naming;

/// <summary>
/// ARCH-0096 convergence point: folds the captured <b>ambient axis bag</b> (axisKey → value, produced by the
/// ARCH-0100 carrier) into an identifier <paramref name="anchor"/> through the <b>one</b>
/// <see cref="IdentifierComposer"/> engine — so every pillar that must encode the ambient into an identifier
/// (job coalesce keys, storage blob keys, …) renders the axes identically, instead of hand-rolling a per-pillar
/// fold (the exact divergence ARCH-0096 exists to eliminate).
///
/// <para>Each axis becomes one <see cref="Particle"/> whose token encodes <c>axisKey=value</c> (so two axes with
/// equal values never collide); particles order deterministically (by axis, ordinal). A null/empty bag returns the
/// anchor unchanged — zero-allocation, byte-identical to the bare identifier.</para>
/// </summary>
public static class AmbientAxisComposer
{
    /// <summary>
    /// Append the ambient <paramref name="axes"/> to <paramref name="anchor"/>. <paramref name="position"/> and
    /// <paramref name="separator"/> let a consumer pick the shape — trailing+"|" for an opaque coalesce key,
    /// leading+"/" for a blob-key tenant prefix.
    /// </summary>
    public static string Append(string anchor, IReadOnlyDictionary<string, string>? axes,
        ParticlePosition position = ParticlePosition.Trailing, string separator = "|")
    {
        if (axes is null || axes.Count == 0) return anchor;
        var particles = new Particle[axes.Count];
        var i = 0;
        foreach (var kv in axes)
            particles[i++] = new Particle(order: 0, axis: kv.Key, value: kv.Key + "=" + kv.Value, position);
        return IdentifierComposer.Compose(anchor, particles, new CompositionPolicy(separator, VerbatimParticleFormatter.Instance));
    }
}
