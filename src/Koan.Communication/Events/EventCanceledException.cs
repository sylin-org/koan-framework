namespace Koan.Communication;

/// <summary>Entity Event publication cancellation carrying the accepted-prefix receipt.</summary>
public sealed class EventCanceledException : OperationCanceledException
{
    internal EventCanceledException(
        string message,
        EventAcceptance acceptance,
        OperationCanceledException innerException,
        CancellationToken cancellationToken)
        : base(message, innerException, cancellationToken)
        => Acceptance = acceptance;

    public EventAcceptance Acceptance { get; }
}
