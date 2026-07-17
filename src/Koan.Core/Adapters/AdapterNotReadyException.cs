namespace Koan.Core.Adapters;

public sealed class AdapterNotReadyException : InvalidOperationException
{
    public AdapterReadinessState CurrentState { get; }

    public string AdapterType { get; }

    public AdapterNotReadyException(string adapterType, AdapterReadinessState state, string message, Exception? inner = null)
        : base(message, inner)
    {
        AdapterType = adapterType;
        CurrentState = state;
    }
}
