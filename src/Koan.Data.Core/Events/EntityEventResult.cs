using System;

namespace Koan.Data.Core.Events;

/// <summary>
/// Represents the outcome of a lifecycle hook execution.
/// </summary>
public readonly struct EntityEventResult
{
    private EntityEventResult(bool isCancelled, string? reason, string? code)
    {
        IsCancelled = isCancelled;
        Reason = reason;
        Code = code;
    }

    /// <summary>
    /// Gets a value indicating whether the operation should be cancelled.
    /// </summary>
    public bool IsCancelled { get; }

    /// <summary>
    /// Optional human readable reason for the cancellation.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Optional machine readable reason code.
    /// </summary>
    public string? Code { get; }

    public static EntityEventResult Proceed() => new(false, null, null);

    public static EntityEventResult Cancel(string reason, string? code = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancellation reason must be provided.", nameof(reason));
        }

        return new EntityEventResult(true, reason, code);
    }
}
