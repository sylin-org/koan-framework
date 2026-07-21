using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Abstractions.Aodb;

/// <summary>
/// ARCH-0102 — one element of a composed <see cref="Aodb"/>: a read-scope predicate plus the
/// <see cref="FieldProvenance"/> of the managed field(s) it references. The provenance decides the store-aware push —
/// a store can enforce the element only if it can keep that field <i>current</i> (ambient-stamped on every write it
/// runs; operation-sourced only where it runs the operation). A <c>FieldFilter</c> shape for the Shared mode; the
/// Particle / Moniker shapes (Container / Database) arrive with later ARCH-0102 phases.
/// </summary>
public sealed record AodbElement(Filter Predicate, FieldProvenance Provenance);
