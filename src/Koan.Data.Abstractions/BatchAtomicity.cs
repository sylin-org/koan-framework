namespace Koan.Data.Abstractions;

/// <summary>Atomicity actually realized by one completed batch execution.</summary>
public enum BatchAtomicity
{
    /// <summary>The batch made no all-or-nothing guarantee.</summary>
    NotGuaranteed,

    /// <summary>Every operation committed at one proved native all-or-nothing boundary.</summary>
    Atomic
}
