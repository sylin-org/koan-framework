using System.ComponentModel;

namespace Koan.Data.Abstractions;

/// <summary>
/// Owns the adapter-neutral wire contract used to persist Entity-family identity.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EntityFamilyStorage
{
    /// <summary>The reserved runtime-type hint carried by a derived Entity record.</summary>
    public const string TypeField = "__koan_type";

    /// <summary>
    /// Rejects a framework or application field that would overwrite Entity-family identity.
    /// </summary>
    public static void EnsureFieldAvailable(string storageName, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if (string.Equals(storageName, TypeField, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{owner} cannot use reserved persistence field '{TypeField}'. " +
                "Choose a distinct storage name; Koan owns that field for Entity-family identity.",
                nameof(storageName));
        }
    }
}
