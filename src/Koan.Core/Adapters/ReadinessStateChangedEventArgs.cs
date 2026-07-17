namespace Koan.Core.Adapters;

public sealed class ReadinessStateChangedEventArgs : EventArgs
{
    public AdapterReadinessState PreviousState { get; }

    public AdapterReadinessState CurrentState { get; }

    public DateTime TimestampUtc { get; }

    public ReadinessStateChangedEventArgs(AdapterReadinessState previous, AdapterReadinessState current)
    {
        PreviousState = previous;
        CurrentState = current;
        TimestampUtc = DateTime.UtcNow;
    }
}
