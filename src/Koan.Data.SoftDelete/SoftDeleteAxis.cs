using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Axes;

namespace Koan.Data.SoftDelete;

/// <summary>
/// ARCH-0102 / ARCH-0101 §7 — soft-delete authored as a <see cref="IDataAxis"/> (the authoring shrink): ONE
/// <see cref="Declare"/> body replaces the three hand-registered seams (the invisible <c>__deleted</c> managed field,
/// the hide-deleted read contributor, and the <c>Delete ⇒ __deleted=true</c> operation override). <b>Byte-identical</b>
/// to the prior raw registration — the expander derives the same RowScoped + indexed + auto-equality-OFF +
/// operation-sourced descriptor, the same cache-excluding non-equality read contributor (wrapped with the
/// <c>AppliesTo</c> = <c>IsSoftDelete</c> check), and the same override. The <c>ArchivedAxis</c> test fixture is this
/// exact shape; discovered at boot via <c>[KoanDiscoverable]</c> on <see cref="IDataAxis"/>.
/// </summary>
public sealed class SoftDeleteAxis : IDataAxis
{
    // visible ⇔ __deleted absent (IS NULL) OR __deleted <> true.  Hidden ⇔ __deleted == true.  NULL-safe (the field is
    // stamped only on delete); both operators push down on relational + document adapters (DATA-0106 §4b).
    private static readonly Filter HideDeleted = Filter.Any(
        Filter.On(FieldPath.Of("__deleted"), FilterOperator.Exists, FilterValue.Of(false)),
        Filter.On(FieldPath.Of("__deleted"), FilterOperator.Ne, FilterValue.Of(true)));

    public void Declare(Axis axis) => axis
        .Named("soft-delete")
        .AppliesTo(static t => SoftDeleteMetadata.IsSoftDelete(t))
        .Field("__deleted", static () => null, typeof(bool))
        .Reads(static _ => SoftDeleteAmbient.ShowDeleted ? null : HideDeleted)   // AppliesTo=IsSoftDelete is added by the framework
        .OnDelete(Logical.SetTrue("__deleted"));
}
