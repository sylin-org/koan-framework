namespace Koan.Data.Core.Lifecycle;

/// <summary>The decision returned by a before-lifecycle handler.</summary>
public readonly struct EntityLifecycleResult
{
    private EntityLifecycleResult(bool isCancelled, string? reason, string? code)
    {
        IsCancelled = isCancelled;
        Reason = reason;
        Code = code;
    }

    public bool IsCancelled { get; }
    public string? Reason { get; }
    public string? Code { get; }

    public static EntityLifecycleResult Proceed() => new(false, null, null);

    public static EntityLifecycleResult Cancel(string reason, string? code = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason must be provided.", nameof(reason));
        return new EntityLifecycleResult(true, reason, code);
    }
}
