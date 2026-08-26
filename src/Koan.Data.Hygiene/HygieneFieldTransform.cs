using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Hygiene;

/// <summary>
/// Normalizes hygiene-annotated string properties on the persisted clone. Write-side only:
/// these transforms are irreversible by design (trimming loses information), so
/// <see cref="ApplyOnRead"/> is an identity no-op — the stored value IS the value.
/// </summary>
internal sealed class HygieneFieldTransform(HygienePropertyBag bag) : IFieldTransform
{
    public void ApplyOnWrite(object entity) => bag.Apply(entity);

    public void ApplyOnRead(object entity) { }
}
