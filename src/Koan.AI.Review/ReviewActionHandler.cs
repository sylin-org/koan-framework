using System.Reflection;

namespace Koan.AI.Review;

/// <summary>
/// Executes review actions on reviewable entities: approve, reject, edit, label, flag.
/// Entity persistence is the caller's responsibility (separation of concerns).
/// </summary>
public interface IReviewActionHandler
{
    /// <summary>Approve the entity as-is.</summary>
    Task ApproveAsync<T>(T entity, string reviewedBy, CancellationToken ct = default) where T : IReviewable;

    /// <summary>Reject the entity with an optional reason.</summary>
    Task RejectAsync<T>(T entity, string reviewedBy, string? reason, CancellationToken ct = default) where T : IReviewable;

    /// <summary>Edit a specific field on the entity.</summary>
    Task EditAsync<T>(T entity, string fieldName, object newValue, string reviewedBy, CancellationToken ct = default) where T : IReviewable;

    /// <summary>Label a specific field on the entity with a value.</summary>
    Task LabelAsync<T>(T entity, string fieldName, object value, string reviewedBy, CancellationToken ct = default) where T : IReviewable;

    /// <summary>Flag the entity with the given flag type.</summary>
    Task FlagAsync<T>(T entity, string flagType, string reviewedBy, CancellationToken ct = default) where T : IReviewable;
}

/// <summary>
/// Default implementation of <see cref="IReviewActionHandler"/>.
/// Mutates the entity's review properties via reflection where necessary.
/// Entity save is the caller's responsibility.
/// </summary>
internal sealed class ReviewActionHandler : IReviewActionHandler
{
    public Task ApproveAsync<T>(T entity, string reviewedBy, CancellationToken ct = default)
        where T : IReviewable
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);

        SetProperty(entity, nameof(IReviewable.ReviewStatus), ReviewStatus.Approved);
        SetProperty(entity, nameof(IReviewable.ReviewedBy), reviewedBy);
        SetProperty(entity, nameof(IReviewable.ReviewedAt), DateTime.UtcNow);

        return Task.CompletedTask;
    }

    public Task RejectAsync<T>(T entity, string reviewedBy, string? reason, CancellationToken ct = default)
        where T : IReviewable
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);

        SetProperty(entity, nameof(IReviewable.ReviewStatus), ReviewStatus.Rejected);
        SetProperty(entity, nameof(IReviewable.ReviewedBy), reviewedBy);
        SetProperty(entity, nameof(IReviewable.ReviewedAt), DateTime.UtcNow);
        SetProperty(entity, nameof(IReviewable.RejectionReason), reason);

        return Task.CompletedTask;
    }

    public Task EditAsync<T>(T entity, string fieldName, object newValue, string reviewedBy, CancellationToken ct = default)
        where T : IReviewable
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);

        var type = entity.GetType();
        var property = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{fieldName}' not found on {type.Name}.");

        // Store the original value if the entity has an Original{FieldName} property.
        var originalPropertyName = $"Original{fieldName}";
        var originalProperty = type.GetProperty(originalPropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (originalProperty is not null && originalProperty.CanWrite)
        {
            var currentValue = property.GetValue(entity);
            originalProperty.SetValue(entity, currentValue);
        }

        property.SetValue(entity, newValue);

        SetProperty(entity, nameof(IReviewable.ReviewStatus), ReviewStatus.Edited);
        SetProperty(entity, nameof(IReviewable.ReviewedBy), reviewedBy);
        SetProperty(entity, nameof(IReviewable.ReviewedAt), DateTime.UtcNow);

        return Task.CompletedTask;
    }

    public Task LabelAsync<T>(T entity, string fieldName, object value, string reviewedBy, CancellationToken ct = default)
        where T : IReviewable
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);

        var type = entity.GetType();
        var property = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{fieldName}' not found on {type.Name}.");

        property.SetValue(entity, value);

        // Labeling is additive — do not change ReviewStatus.
        // Update reviewer info for audit trail.
        SetProperty(entity, nameof(IReviewable.ReviewedBy), reviewedBy);
        SetProperty(entity, nameof(IReviewable.ReviewedAt), DateTime.UtcNow);

        return Task.CompletedTask;
    }

    public Task FlagAsync<T>(T entity, string flagType, string reviewedBy, CancellationToken ct = default)
        where T : IReviewable
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagType);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);

        SetProperty(entity, nameof(IReviewable.ReviewStatus), ReviewStatus.Flagged);
        SetProperty(entity, nameof(IReviewable.ReviewedBy), reviewedBy);
        SetProperty(entity, nameof(IReviewable.ReviewedAt), DateTime.UtcNow);

        // If the entity has a Flags property (List<string>), add the flag type.
        var type = entity.GetType();
        var flagsProperty = type.GetProperty("Flags", BindingFlags.Public | BindingFlags.Instance);
        if (flagsProperty is not null && flagsProperty.PropertyType == typeof(List<string>))
        {
            var flags = flagsProperty.GetValue(entity) as List<string>;
            if (flags is not null && !flags.Contains(flagType))
            {
                flags.Add(flagType);
            }
        }

        return Task.CompletedTask;
    }

    // ── Internal Helpers ──

    private static void SetProperty<T>(T entity, string propertyName, object? value)
    {
        var type = entity!.GetType();
        var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        if (property is not null && property.CanWrite)
        {
            property.SetValue(entity, value);
        }
    }
}
