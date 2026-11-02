namespace Koan.Data.Core.Commands;

/// <summary>What kind of refusal a <see cref="Reason"/> is. A surface maps each once.</summary>
public enum ReasonKind
{
    /// <summary>A value the sender gave cannot be taken.</summary>
    Invalid,

    /// <summary>What it acts on does not exist, or this sender may not know that it does.</summary>
    Absent,

    /// <summary>This sender may not do it, or not now.</summary>
    Gated,

    /// <summary>It disagrees with what is already there.</summary>
    Conflict,
}
