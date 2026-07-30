namespace Koan.Data.Abstractions;

public sealed class AdditionalResultChannelsNotSupportedException : NotSupportedException
{
    public AdditionalResultChannelsNotSupportedException(string operation)
        : base($"Registered operation '{operation}' returned an additional result channel. Split the read or use a provider-native Direct surface.")
        => Operation = operation;

    public string Operation { get; }
}
