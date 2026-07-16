namespace Koan.Communication;

/// <summary>A corrective Transport publication failure carrying the operation's bounded partial acceptance.</summary>
public sealed class TransportException : Exception
{
    public enum FailureKind
    {
        NoReceivers,
        Serialization,
        PayloadTooLarge,
        SourceFailed,
        ProviderUnavailable,
        SettlementUnavailable
    }

    internal TransportException(
        FailureKind failure,
        string message,
        TransportAcceptance acceptance,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        Acceptance = acceptance;
    }

    public FailureKind Failure { get; }
    public TransportAcceptance Acceptance { get; }
}
