namespace Koan.Data.Abstractions;

/// <summary>Guarantees a native batch can promise before it begins execution.</summary>
[Flags]
public enum BatchExecutionCapabilities
{
    None = 0,

    /// <summary>The batch can commit all queued operations at one native all-or-nothing boundary.</summary>
    Atomic = 1,

    /// <summary>The batch returns one ordered outcome for every queued operation.</summary>
    CompleteItemOutcomes = 2
}
