namespace Koan.Communication;

/// <summary>A canceled Transport publication carrying the operation's bounded accepted prefix.</summary>
public sealed class TransportCanceledException : OperationCanceledException
{
    internal TransportCanceledException(
        string message,
        TransportAcceptance acceptance,
        OperationCanceledException innerException,
        CancellationToken cancellationToken)
        : base(message, innerException, cancellationToken)
        => Acceptance = acceptance;

    public TransportAcceptance Acceptance { get; }
}
