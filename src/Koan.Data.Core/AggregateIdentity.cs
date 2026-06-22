using Koan.Core;

namespace Koan.Data.Core;

/// <summary>
/// Shared identifier generation — the single id-gen used by both the write-stamp pipeline
/// (<see cref="Pipeline.IdentityWriteStamp"/>) and the transaction-path <see cref="IAggregateIdentityManager"/>,
/// so the rule cannot drift between the two. Generates a GUID v7 (Guid id) or a sortable string id when the
/// identifier is still at its default; otherwise leaves the caller's id untouched.
/// </summary>
internal static class AggregateIdentity
{
    public static void Ensure(object? entity, AggregateMetadata.IdSpec? spec)
    {
        if (entity is null || spec?.Prop is not { } prop || !prop.CanWrite) return;

        var current = prop.GetValue(entity);
        if (spec.IsString)
        {
            if (current is not string s || string.IsNullOrWhiteSpace(s))
                prop.SetValue(entity, StringId.New());
        }
        else if (spec.IsGuid)
        {
            if (current is Guid g && g == default)
                prop.SetValue(entity, Guid.CreateVersion7());
        }
    }
}
