using Koan.Canon.Domain.Metadata;
using Koan.Data.Core.Model;

namespace Koan.Canon.Domain.Model;

/// <summary>
/// Base type for canonical aggregates. Extends the Koan entity base while tracking canon metadata and lifecycle.
/// </summary>
/// <typeparam name="TModel">Concrete canonical entity type.</typeparam>
public abstract class CanonEntity<TModel> : Entity<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    private CanonMetadata _metadata = new();
    private CanonStatus _status = CanonStatus.Active;

    /// <summary>
    /// Canon metadata describing sources, policies, and lineage for this entity.
    /// </summary>
    public CanonMetadata Metadata
    {
        get => _metadata;
        set => _metadata = value ?? new CanonMetadata();
    }

    /// <summary>
    /// Lifecycle state for the canonical entity.
    /// </summary>
    public CanonStatus Status
    {
        get => _status;
        private set
        {
            _status = value;
            _metadata.Touch();
        }
    }

    /// <summary>
    /// Ensures metadata remains aligned with the canonical identifier.
    /// </summary>
    public override string Id
    {
        get
        {
            var id = base.Id;
            if (!Metadata.HasCanonicalId)
            {
                Metadata.AssignCanonicalId(id);
            }

            return id;
        }
        set
        {
            base.Id = value;
            Metadata.AssignCanonicalId(value);
        }
    }

    /// <summary>
    /// Marks the entity as superseded by another canonical identifier.
    /// </summary>
    /// <param name="replacementId">Replacement canonical identifier.</param>
    /// <param name="reason">Optional reason for the supersession.</param>
    public void MarkSuperseded(string replacementId, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(replacementId))
        {
            throw new ArgumentException("Replacement identifier must be provided.", nameof(replacementId));
        }

        Status = CanonStatus.Superseded;
        Metadata.Lineage.MarkSupersededBy(replacementId, reason);
    }

    /// <summary>
    /// Marks the entity as archived.
    /// </summary>
    /// <param name="notes">Optional archival notes.</param>
    public void Archive(string? notes = null)
    {
        Status = CanonStatus.Archived;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Metadata.SetTag("archive:notes", notes);
        }
    }

    /// <summary>
    /// Restores the entity to an active state, clearing archival notes if present.
    /// </summary>
    public void Restore()
    {
        Status = CanonStatus.Active;
        Metadata.RemoveTag("archive:notes");
    }

    /// <summary>
    /// Flags the entity as withdrawn.
    /// </summary>
    /// <param name="reason">Reason for withdrawal.</param>
    public void Withdraw(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Withdrawal reason must be provided.", nameof(reason));
        }

        Status = CanonStatus.Withdrawn;
        Metadata.SetTag("withdrawn:reason", reason);
    }
}
