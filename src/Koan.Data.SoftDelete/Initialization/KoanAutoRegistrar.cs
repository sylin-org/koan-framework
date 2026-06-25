using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;

namespace Koan.Data.SoftDelete.Initialization;

/// <summary>
/// Lights up soft-delete when <c>Koan.Data.SoftDelete</c> is referenced (Reference = Intent, ARCH-0101 §4). Registers
/// the three contributors the data core composes generically — never naming "soft-delete":
/// <list type="bullet">
/// <item>the invisible <c>__deleted</c> managed field (DATA-0105) — absent on normal writes, set by the override on delete;</item>
/// <item>the hide-deleted read-visibility predicate (DATA-0106) — a <see cref="IReadFilterContributor"/>;</item>
/// <item>the <c>Delete ⇒ __deleted=true</c> operation-semantics override (ARCH-0101 §4).</item>
/// </list>
/// Not referencing the package leaves all three seams empty (structural no-op). The per-entity opt-in is
/// <see cref="SoftDeleteAttribute"/>; a non-<c>[SoftDelete]</c> entity is byte-identical (every <c>AppliesTo</c> is false).
/// </summary>
public sealed class KoanAutoRegistrar : KoanModule
{
    public override string Id => "Koan.Data.SoftDelete";

    public override void Register(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadFilterContributor, SoftDeleteReadContributor>());

        // The invisible __deleted discriminator (no POCO property). ValueProvider returns null ⇒ absent on normal
        // writes; the OnDelete override stamps it true (through the UNGUARDED override write channel, so it is injected
        // but never conflict-guarded — it is a mutable state field, not an isolation field). AutoReadFilter=false ⇒ the
        // built-in equality contributor never derives an equality on it; the read contributor supplies the visibility predicate.
        // RequiredCapability = RowScoped: the adapter must persist AND filter the managed __deleted discriminator at the
        // store (DataCaps.Isolation.RowScoped is axis-free — "persists + filters a managed field", exactly what soft-delete
        // needs). On an in-memory-evaluating adapter (JSON / InMemory, FilterSupport.Full) the managed field cannot be
        // evaluated (it is not a POCO property), so a [SoftDelete] entity there FAILS CLOSED with the actionable
        // "does not announce" message rather than throwing an opaque "pushed down" error mid-read.
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
            StorageName: "__deleted",
            ClrType: typeof(bool),
            ValueProvider: static () => null,
            AppliesTo: static t => SoftDeleteMetadata.IsSoftDelete(t),
            RequiredCapability: DataCaps.Isolation.RowScoped,
            Indexed: true,
            AutoReadFilter: false,
            // ARCH-0102 §3: set on Delete (the override below), never ambient-stamped on a normal write — so a
            // secondary store (e.g. the independent vector index) that never ran the delete cannot keep it current.
            Provenance: FieldProvenance.OperationSourced));

        OperationOverrideRegistry.Register(new OperationOverrideDescriptor(
            Field: "__deleted",
            OnDeleteValue: true,
            AppliesTo: static t => SoftDeleteMetadata.IsSoftDelete(t)));
    }
}
