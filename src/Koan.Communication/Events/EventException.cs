namespace Koan.Communication;

/// <summary>A typed Entity Event publication failure carrying the accepted-prefix receipt.</summary>
public class EventException : Exception
{
    internal EventException(
        FailureKind failure,
        string message,
        EventAcceptance acceptance,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        Acceptance = acceptance;
    }

    public FailureKind Failure { get; }
    public EventAcceptance Acceptance { get; }

    public enum FailureKind
    {
        DetailsRequired,
        Serialization,
        PayloadTooLarge,
        ProviderUnavailable,
        SourceFailed
    }
}
