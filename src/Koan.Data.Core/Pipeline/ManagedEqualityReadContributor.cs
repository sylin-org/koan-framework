using System.Collections.Generic;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The built-in default <see cref="IReadFilterContributor"/> (DATA-0106 §2) — the re-homing of the facade's former
/// bespoke <c>ManagedReadFilter</c>. It reproduces that tri-state <b>verbatim</b>: iterate the managed descriptors
/// applicable to the type that opt into the auto read-filter (<see cref="ManagedFieldDescriptor.AutoReadFilter"/>),
/// emit a scalar <c>Filter.Eq(StorageName, value)</c> only for descriptors whose ambient value is non-null (off / host
/// ⇒ skip), and unwrap a single predicate. So tenancy's read-filter now <i>flows through the seam</i> — golden, pure
/// registration, byte-identical — without tenancy changing a line.
///
/// <para>Its per-descriptor isolation capabilities are <b>not</b> declared here (<see cref="RequiredCapability"/> is
/// <c>null</c>): they live on each <see cref="ManagedFieldDescriptor.RequiredCapability"/> and are enforced by the
/// facade's managed-descriptor inspection (the write-stamp and read paths share one fail-closed check). A descriptor
/// with <c>AutoReadFilter == false</c> is skipped entirely here — it supplies its own (non-equality) predicate via a
/// separate contributor and would wrongly conjoin an equality if included.</para>
/// </summary>
internal sealed class ManagedEqualityReadContributor : IReadFilterContributor
{
    public Filter? ReadFilter(System.Type entityType)
    {
        if (ManagedFieldRegistry.IsEmpty) return null;            // structurally absent ⇒ byte-identical fast path
        var managed = ManagedFieldRegistry.ForType(entityType);
        List<Filter>? preds = null;
        foreach (var d in managed)
        {
            if (!d.AutoReadFilter) continue;                      // a non-equality axis supplies its own predicate
            var v = d.ValueProvider();
            if (v is null) continue;                              // off / host ⇒ unfiltered (nothing was stamped)
            (preds ??= new()).Add(Filter.Eq(d.StorageName, v));
        }
        if (preds is null) return null;
        return preds.Count == 1 ? preds[0] : Filter.All(preds.ToArray());
    }

    // The equality contributor's capability requirements ARE its descriptors' (ManagedFieldDescriptor.RequiredCapability),
    // enforced by the facade's managed-descriptor inspection — so it declares none of its own here.
    public Capability? RequiredCapability => null;
}
